#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Updates or verifies the product version badge in README.md.

.DESCRIPTION
README.md shows the product version as a shields.io badge. The pre-commit hook calls this script
with Staged so that every commit on every branch carries the version of that commit, for example
202611.2-dev on dev or 202611.2 for the release commit on main.

Without Version, the version of the next commit is calculated with
Get-ProductVersion.ps1 -NextCommit.

.PARAMETER Version
The product version to show. Defaults to the version of the next commit.

.PARAMETER Staged
Updates the staged README.md in the Git index in addition to the working tree, without staging
other unstaged README changes. With Verify, the staged README.md is checked.

.PARAMETER Verify
Throws when the badge does not show the version instead of changing README.md.

.PARAMETER Path
The README file to process. Defaults to README.md in the repository root. Staged requires the
default path.

.OUTPUTS
System.String. The expected product version.

.EXAMPLE
./scripts/Update-ReadmeVersion.ps1 -Staged

Updates the badge in the working tree and the index before a commit.

.EXAMPLE
./scripts/Update-ReadmeVersion.ps1 -Verify -Version 202611.2

Verifies that README.md shows the release version 202611.2.

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

[CmdletBinding()]
[OutputType([string])]
param(
    [ValidatePattern('^\d{6}\.\d+(-[0-9A-Za-z-]+)?$')]
    [string]$Version,

    [switch]$Staged,

    [switch]$Verify,

    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$badgePattern = '(?<prefix>https://img\.shields\.io/badge/version-)(?<encoded>[^"]+?)(?<middle>-0A66C2" alt="Version )(?<version>[^"]+)(?<suffix>")'

<#
.SYNOPSIS
Escapes a value for the message part of a static shields.io badge URL.
#>
function ConvertTo-BadgeText {
    param([Parameter(Mandatory)][string]$Value)

    $Value.Replace('-', '--').Replace('_', '__')
}

<#
.SYNOPSIS
Returns the README text with the version badge set to the version.
#>
function Set-BadgeVersion {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Value
    )

    if (-not [regex]::IsMatch($Text, $badgePattern)) {
        throw 'README.md contains no version badge (https://img.shields.io/badge/version-...-0A66C2).'
    }
    [regex]::Replace($Text, $badgePattern, {
        param($match)
        $match.Groups['prefix'].Value + (ConvertTo-BadgeText -Value $Value) +
            $match.Groups['middle'].Value + $Value + $match.Groups['suffix'].Value
    }, [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(1))
}

<#
.SYNOPSIS
Runs a Git command in the repository and fails on a non-zero exit code.
#>
function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git -C $repositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed."
    }
    $output
}

<#
.SYNOPSIS
Writes the staged README.md to a temporary file in its working-tree form and returns the path.
#>
function Export-StagedReadme {
    $output = Invoke-Git -Arguments @('checkout-index', '--temp', '--', 'README.md')
    Join-Path $repositoryRoot (($output -split "`t")[0])
}

if (-not $Version) {
    $Version = & (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1') -NextCommit
}
$defaultPath = Join-Path $repositoryRoot 'README.md'
if (-not $Path) {
    $Path = $defaultPath
}
elseif ($Staged) {
    throw 'Staged cannot be combined with Path.'
}

if ($Verify) {
    $text = if ($Staged) {
        $stagedFile = Export-StagedReadme
        try { [IO.File]::ReadAllText($stagedFile) } finally { Remove-Item -LiteralPath $stagedFile -Force }
    }
    else {
        [IO.File]::ReadAllText($Path)
    }
    $match = [regex]::Match($text, $badgePattern)
    if (-not $match.Success) {
        throw 'README.md contains no version badge (https://img.shields.io/badge/version-...-0A66C2).'
    }
    if ($match.Groups['version'].Value -cne $Version -or
        $match.Groups['encoded'].Value -cne (ConvertTo-BadgeText -Value $Version)) {
        throw "The README.md version badge shows '$($match.Groups['version'].Value)' instead of '$Version'. Run scripts/Update-ReadmeVersion.ps1 -Staged."
    }
    return $Version
}

$current = [IO.File]::ReadAllText($Path)
$updated = Set-BadgeVersion -Text $current -Value $Version
if ($updated -cne $current) {
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
}

if ($Staged) {
    # Rewrite only the staged blob so that unrelated unstaged README changes stay unstaged.
    $indexEntry = Invoke-Git -Arguments @('ls-files', '--stage', '--', 'README.md')
    if ($indexEntry) {
        $mode = ($indexEntry -split '\s+')[0]
        $stagedFile = Export-StagedReadme
        try {
            $stagedText = [IO.File]::ReadAllText($stagedFile)
            $stagedUpdated = Set-BadgeVersion -Text $stagedText -Value $Version
            if ($stagedUpdated -cne $stagedText) {
                [IO.File]::WriteAllText($stagedFile, $stagedUpdated, [Text.UTF8Encoding]::new($false))
                $blob = Invoke-Git -Arguments @('hash-object', '-w', '--path', 'README.md', '--', $stagedFile)
                Invoke-Git -Arguments @('update-index', '--cacheinfo', "$mode,$blob,README.md") | Out-Null
            }
        }
        finally {
            Remove-Item -LiteralPath $stagedFile -Force
        }
    }
}

$Version
