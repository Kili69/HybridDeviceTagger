#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Prepares the release changelog on dev, verifies a promotion, or finalizes metadata on main.

.DESCRIPTION
Without switches, moves all Unreleased entries to the next Git-derived release section on dev.
Verify rejects an unprepared changelog and returns the release notes. Finalize verifies the
prepared section on main, updates both README badges without a branch suffix, and records that
metadata change in the release section. Finalize is idempotent and does not commit or tag.

.PARAMETER Verify
Verifies the prepared changelog without modifying files.

.PARAMETER Finalize
Finalizes only README badges and changelog release metadata on main.

.PARAMETER Version
Expected core version for verification. Defaults to the Git-derived version of main.

.PARAMETER ChangelogPath
Changelog to verify, allowing trusted CI code to inspect a PR without executing its scripts.

.EXAMPLE
./scripts/Prepare-Release.ps1

Prepares the next release on dev before committing and opening a PR to main.

.EXAMPLE
./scripts/Prepare-Release.ps1 -Finalize

Finalizes the prepared release metadata after the dev PR has been merged into main.

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

[CmdletBinding(DefaultParameterSetName = 'Prepare')]
[OutputType([string])]
param(
    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [switch]$Verify,

    [Parameter(Mandatory, ParameterSetName = 'Finalize')]
    [switch]$Finalize,

    [Parameter(ParameterSetName = 'Verify')]
    [ValidatePattern('^\d{6}\.\d+$')]
    [string]$Version,

    [Parameter(ParameterSetName = 'Verify')]
    [string]$ChangelogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionInfo = & (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1') -Detailed -NextCommit:($PSCmdlet.ParameterSetName -eq 'Prepare')
if (-not $Version) {
    $Version = $versionInfo.CoreVersion
}
if (-not $ChangelogPath) {
    $ChangelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
}
if (-not $Verify) {
    $expectedBranch = if ($Finalize) { 'main' } else { 'dev' }
    if ($versionInfo.Branch -cne $expectedBranch) {
        throw "This operation requires '$expectedBranch', not '$($versionInfo.Branch)'."
    }
}

$text = [IO.File]::ReadAllText($ChangelogPath)
$unreleased = [regex]::Match($text, '(?ms)^## \[Unreleased\]\r?\n(?<entries>.*?)(?=^## |\z)')
if (-not $unreleased.Success) {
    throw 'CHANGELOG.md must contain an Unreleased section.'
}
$releasePattern = "(?ms)^## \[$([regex]::Escape($Version))\] - (?<date>\d{4}-\d{2}-\d{2})\r?`n(?<notes>.*?)(?=^## |\z)"
$release = [regex]::Match($text, $releasePattern)

if ($PSCmdlet.ParameterSetName -eq 'Prepare') {
    if ($release.Success) {
        throw "Release $Version is already prepared. Use -Verify or add changes for the next release."
    }
    $entries = $unreleased.Groups['entries'].Value.Trim()
    if (-not [regex]::IsMatch($entries, '(?m)^- \S')) {
        throw 'Add release entries below Unreleased before preparing a release.'
    }
    $date = [datetime]::UtcNow.ToString('yyyy-MM-dd')
    $replacement = "## [Unreleased]`n`n## [$Version] - $date`n`n$entries`n`n"
    $text = $text.Remove($unreleased.Index, $unreleased.Length).Insert($unreleased.Index, $replacement)
    [IO.File]::WriteAllText($ChangelogPath, $text, [Text.UTF8Encoding]::new($false))
    return $Version
}

$firstRelease = [regex]::Match($text, '(?m)^## \[(?!Unreleased\])')
$releaseDate = [datetime]::MinValue
if ($unreleased.Groups['entries'].Value.Trim() -or
    -not $release.Success -or $firstRelease.Index -ne $release.Index -or
    -not [datetime]::TryParseExact($release.Groups['date'].Value, 'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$releaseDate) -or
    -not [regex]::IsMatch($release.Groups['notes'].Value, '(?m)^- \S')) {
    throw "Prepare CHANGELOG.md on dev: Unreleased must be empty and the first release section must be [$Version] with a valid date and release entries."
}
if ($Verify) {
    return $release.Groups['notes'].Value.Trim()
}

$metadataEntry = '- Finalized the main version badges for the tested release installation package.'
$notes = $release.Groups['notes'].Value.Trim()
if (-not $notes.Contains($metadataEntry)) {
    $changedHeading = [regex]::Match($notes, '(?m)^### Changed\r?$')
    if ($changedHeading.Success) {
        $notes = $notes.Insert($changedHeading.Index + $changedHeading.Length, "`n`n$metadataEntry")
    }
    else {
        $notes += "`n`n### Changed`n`n$metadataEntry"
    }
    $replacement = "## [$Version] - $($release.Groups['date'].Value)`n`n$notes`n`n"
    $text = $text.Remove($release.Index, $release.Length).Insert($release.Index, $replacement)
    [IO.File]::WriteAllText($ChangelogPath, $text, [Text.UTF8Encoding]::new($false))
}
& (Join-Path $PSScriptRoot 'Update-ReadmeVersion.ps1') -Version $Version | Out-Null
$Version
