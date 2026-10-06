#Requires -Version 7.2
# Project-Version: 0.1.20261006.1
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
