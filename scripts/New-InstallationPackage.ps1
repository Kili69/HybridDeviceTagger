#Requires -Version 7.2
# Project-Version: 0.1.20260917.1
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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
    Copy-Item (Join-Path $repositoryRoot 'readme.md') $packageRoot
    Copy-Item (Join-Path $repositoryRoot 'CHANGELOG.md') $packageRoot

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