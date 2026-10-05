#Requires -Version 7.2
# Project-Version: 0.1.20261005.3
# Author: andreas.lucas@outlook.com (aka Kili)

<#
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

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

[xml]$buildProperties = Get-Content (Join-Path $repositoryRoot 'Directory.Build.props')
$properties = $buildProperties.Project.PropertyGroup[0]
$version = '{0}.{1}.{2}.{3}' -f @(
    $properties.VersionMajor.InnerText,
    $properties.VersionMinor.InnerText,
    $properties.VersionDate.InnerText,
    $properties.VersionCounter.InnerText
)
$archivePath = Join-Path $OutputDirectory "HybridDeviceTagger-$version.zip"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-package-$([guid]::NewGuid())"
$packageRoot = Join-Path $temporaryRoot 'HybridDeviceTagger'
$publishPath = Join-Path $temporaryRoot 'publish'

try {
    $null = New-Item -ItemType Directory -Path $packageRoot -Force
    Copy-Item (Join-Path $repositoryRoot 'Deployment') $packageRoot -Recurse
    Copy-Item (Join-Path $repositoryRoot 'README.md') $packageRoot
    Copy-Item (Join-Path $repositoryRoot 'DEVELOPER.md') $packageRoot
    Copy-Item (Join-Path $repositoryRoot 'CHANGELOG.md') $packageRoot
    Copy-Item (Join-Path $repositoryRoot 'LICENSE') $packageRoot

    & dotnet publish `
        (Join-Path $repositoryRoot 'src/HybridDeviceTagger/HybridDeviceTagger.csproj') `
        --configuration Release `
        --output $publishPath
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }

    $functionPackagePath = Join-Path $packageRoot 'Deployment/HybridDeviceTagger.zip'
    Compress-Archive `
        -Path (Join-Path $publishPath '*') `
        -DestinationPath $functionPackagePath `
        -CompressionLevel Optimal

    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }
    Compress-Archive `
        -Path $packageRoot `
        -DestinationPath $archivePath `
        -CompressionLevel Optimal

    Write-Output $archivePath
}
finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item $temporaryRoot -Recurse -Force
    }
}