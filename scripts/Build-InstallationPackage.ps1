#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Builds the HybridDeviceTagger installation package and its release archive.

.DESCRIPTION
The script:

1. calculates the product version with Get-ProductVersion.ps1 (<YYYY><MM>.<counter>, plus the
   branch name outside main);
2. runs the unit tests unless SkipTests is specified;
3. publishes the Function in Release configuration with that version and stores it as
   installationspackage/function/HybridDeviceTagger.zip, so the installer no longer needs the
   .NET SDK;
4. copies installationspackage to <OutputPath>/HybridDeviceTagger-<version>, stamps the version
   into every PowerShell file and the module manifest, adds version.json and LICENSE, and
   compresses the folder to <OutputPath>/HybridDeviceTagger-<version>.zip.

With Release, the build additionally requires the main branch, a clean working tree, and a
CHANGELOG.md section for the version, and creates the annotated Git tag v<version> afterwards.
Push the tag with git push origin v<version>.

.PARAMETER OutputPath
Directory that receives the staged package folder and the release archive. Defaults to
artifacts in the repository root.

.PARAMETER SkipTests
Skips dotnet test.

.PARAMETER Release
Builds an official release from main and tags HEAD with the release version.

.EXAMPLE
./scripts/Build-InstallationPackage.ps1

Builds a test package, for example artifacts/HybridDeviceTagger-202611.1-dev.zip.

.EXAMPLE
./scripts/Build-InstallationPackage.ps1 -Release

Builds and tags the release, for example v202611.1.

.NOTES
Copyright 2026 Andreas Lucas

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
SPDX-License-Identifier: Apache-2.0
#>

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The build script writes colored progress output.'
)]
[CmdletBinding()]
param(
    [string]$OutputPath,

    [switch]$SkipTests,

    [switch]$Release
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageSourcePath = Join-Path $repositoryRoot 'installationspackage'
$projectPath = Join-Path $repositoryRoot 'src/HybridDeviceTagger/HybridDeviceTagger.csproj'
$solutionPath = Join-Path $repositoryRoot 'HybridDeviceTagger.sln'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts'
}
# Local answers of the guided installer and earlier build output never belong in a package.
$excludedPackageFiles = @('installation-settings.json')

<#
.SYNOPSIS
Writes a highlighted build step.
#>
function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

<#
.SYNOPSIS
Runs an external command and fails the build on a nonzero exit code.
#>
function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

<#
.SYNOPSIS
Verifies the preconditions of an official release.
#>
function Assert-ReleasePrecondition {
    param([Parameter(Mandatory)]$Version)

    if ($Version.Branch -cne 'main') {
        throw "Releases are built from main only. The current branch is '$($Version.Branch)'."
    }
    $status = & git -C $repositoryRoot status --porcelain
    if ($LASTEXITCODE -ne 0 -or $status) {
        throw 'Releases require a clean working tree. Commit or stash all changes first.'
    }
    $changelogPattern = "^## \[$([regex]::Escape($Version.CoreVersion))\] - \d{4}-\d{2}-\d{2}$"
    if (-not (Select-String -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Pattern $changelogPattern -Quiet)) {
        throw "CHANGELOG.md needs a '## [$($Version.CoreVersion)] - <yyyy-MM-dd>' section. Move the Unreleased entries into it and commit."
    }
}

<#
.SYNOPSIS
Replaces the source version markers in the staged package with the product version.
#>
function Write-PackageVersionMarker {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Version
    )

    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File -Include '*.ps1', '*.psm1', '*.psd1') {
        $text = [IO.File]::ReadAllText($file.FullName)
        $text = $text.Replace('# Project-Version: source', "# Project-Version: $($Version.ProductVersion)")
        if ($file.Extension -eq '.psd1') {
            $text = $text -replace "ModuleVersion = '[^']*'", "ModuleVersion = '$($Version.CoreVersion)'"
            if ($Version.ModulePrerelease) {
                $text = $text -replace '(?m)^(\s*)PSData = @\{', "`$1PSData = @{`n`$1    Prerelease = '$($Version.ModulePrerelease)'"
            }
        }
        [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
    }
}

Push-Location $repositoryRoot
try {
    Write-Step 'Calculating the product version'
    $version = & (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1') -Detailed
    Write-Host "Product version: $($version.ProductVersion) (assembly $($version.AssemblyVersion))"
    if ($Release) {
        Assert-ReleasePrecondition -Version $version
    }
    $versionProperty = "-p:ProductVersion=$($version.ProductVersion)"

    if (-not $SkipTests) {
        Write-Step 'Running unit tests'
        Invoke-Checked -FilePath 'dotnet' -Arguments @('test', $solutionPath, '--configuration', 'Release', '--nologo', $versionProperty)
    }

    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "HybridDeviceTagger-build-$([guid]::NewGuid())"
    try {
        Write-Step 'Publishing the Function'
        $publishPath = Join-Path $temporaryRoot 'publish'
        Invoke-Checked -FilePath 'dotnet' -Arguments @(
            'publish', $projectPath, '--configuration', 'Release', '--output', $publishPath, '--nologo', $versionProperty)

        $functionDirectory = Join-Path $packageSourcePath 'function'
        $null = New-Item -ItemType Directory -Path $functionDirectory -Force
        $functionPackagePath = Join-Path $functionDirectory 'HybridDeviceTagger.zip'
        Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $functionPackagePath -CompressionLevel Optimal -Force

        $versionInformation = [ordered]@{
            productVersion = $version.ProductVersion
            assemblyVersion = $version.AssemblyVersion
            branch = $version.Branch
            commit = (& git rev-parse HEAD).Trim()
            builtAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json
        Set-Content -LiteralPath (Join-Path $packageSourcePath 'version.json') -Value $versionInformation -Encoding utf8NoBOM

        Write-Step 'Staging the installation package'
        $packageName = "HybridDeviceTagger-$($version.ProductVersion)"
        $stagingPath = Join-Path $OutputPath $packageName
        if (Test-Path -LiteralPath $stagingPath) {
            Remove-Item -LiteralPath $stagingPath -Recurse -Force
        }
        $null = New-Item -ItemType Directory -Path $stagingPath -Force
        Get-ChildItem -LiteralPath $packageSourcePath -Force |
            Where-Object Name -NotIn $excludedPackageFiles |
            Copy-Item -Destination $stagingPath -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $stagingPath
        Write-PackageVersionMarker -Path $stagingPath -Version $version

        $archivePath = Join-Path $OutputPath "$packageName.zip"
        Compress-Archive -Path (Join-Path $stagingPath '*') -DestinationPath $archivePath -CompressionLevel Optimal -Force
        Write-Host "Package folder: $stagingPath" -ForegroundColor Green
        Write-Host "Release archive: $archivePath" -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }

    if ($Release -and -not $version.IsTaggedRelease) {
        Write-Step "Tagging the release $($version.ReleaseTag)"
        Invoke-Checked -FilePath 'git' -Arguments @('tag', '--annotate', $version.ReleaseTag, '--message', "HybridDeviceTagger $($version.CoreVersion)")
        Write-Host "Push the tag with: git push origin $($version.ReleaseTag)" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
