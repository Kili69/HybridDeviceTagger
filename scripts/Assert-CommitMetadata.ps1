#Requires -Version 7.2
# Project-Version: 0.1.20260905.8
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

[CmdletBinding()]
param(
    [switch]$Staged
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repositoryRoot
try {
    $diffArguments = @('diff', '--name-only')
    if ($Staged) {
        $diffArguments += '--cached'
    }

    $changedFiles = @(& git @diffArguments)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect changed files.'
    }
    if (-not $Staged) {
        $changedFiles += @(& git ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to inspect untracked files.'
        }
    }

    $requiredFiles = @('Directory.Build.props', 'CHANGELOG.md')
    $missingFiles = @($requiredFiles | Where-Object { $_ -notin $changedFiles })
    if ($missingFiles.Count -gt 0) {
        throw "Every commit must update: $($missingFiles -join ', ')."
    }

    [xml]$currentProps = Get-Content 'Directory.Build.props' -Raw
    $versionProperties = @('VersionMajor', 'VersionMinor', 'VersionDate', 'VersionCounter')
    $currentVersionParts = @($versionProperties | ForEach-Object {
        $currentProps.SelectSingleNode("/Project/PropertyGroup/$_").InnerText
    })
    $currentVersion = $currentVersionParts -join '.'
    $currentVersionNumbers = @($currentVersionParts | ForEach-Object { [long]$_ })

    $previousPropsText = & git show 'HEAD:Directory.Build.props' 2>$null
    if ($LASTEXITCODE -eq 0) {
        [xml]$previousProps = $previousPropsText -join [Environment]::NewLine
        $previousVersionParts = @($versionProperties | ForEach-Object {
            $previousProps.SelectSingleNode("/Project/PropertyGroup/$_").InnerText
        })
        $previousVersion = $previousVersionParts -join '.'
        $previousVersionNumbers = @($previousVersionParts | ForEach-Object { [long]$_ })
        $versionIncreased = $false
        for ($index = 0; $index -lt $currentVersionNumbers.Count; $index++) {
            if ($currentVersionNumbers[$index] -eq $previousVersionNumbers[$index]) {
                continue
            }
            $versionIncreased = $currentVersionNumbers[$index] -gt $previousVersionNumbers[$index]
            break
        }
        if (-not $versionIncreased) {
            throw "Directory.Build.props must advance the version beyond $previousVersion."
        }
    }

    $versionPattern = [regex]::Escape($currentVersion)
    $versionDate = [datetime]::ParseExact(
        $currentVersionParts[2],
        'yyyyMMdd',
        [Globalization.CultureInfo]::InvariantCulture).ToString('yyyy-MM-dd')
    if (-not (Select-String -Path 'CHANGELOG.md' -Pattern "^## \[$versionPattern\] - $versionDate$" -Quiet)) {
        throw "CHANGELOG.md must contain a $versionDate section for version $currentVersion."
    }

    Write-Output "Commit metadata valid for version $currentVersion."
}
finally {
    Pop-Location
}
