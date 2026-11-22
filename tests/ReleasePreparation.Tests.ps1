#Requires -Version 7.2

<#
.SYNOPSIS
Tests release preparation, promotion rejection, and idempotent main metadata finalization.

.EXAMPLE
pwsh -NoProfile -File tests\ReleasePreparation.Tests.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) "HybridDeviceTagger-ReleaseTests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
try {
    foreach ($name in 'Prepare-Release.ps1', 'Get-ProductVersion.ps1', 'Update-ReadmeVersion.ps1', 'Assert-CommitMetadata.ps1') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts\$name") -Destination (Join-Path $fixture 'scripts')
    }
    function Invoke-TestGit {
        param([string[]]$Arguments)
        $output = & git -C $fixture @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Fixture Git failed: $($Arguments -join ' ')" }
        $output
    }
    function Assert-Rejected {
        param([scriptblock]$Action, [string]$Message)
        $failure = ''
        try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
        if ($failure -notlike $Message) { throw "Expected '$Message', got '$failure'." }
    }
    Invoke-TestGit @('init', '--initial-branch=dev') | Out-Null
    Invoke-TestGit @('config', 'user.name', 'Release tests')
    Invoke-TestGit @('config', 'user.email', 'release-tests@example.invalid')
    Invoke-TestGit @('config', 'core.hooksPath', '.disabled-test-hooks')
    $changelogPath = Join-Path $fixture 'CHANGELOG.md'
    $initial = "# Changelog`n`n## [Unreleased]`n`n### Changed`n`n- First change.`n- Second change.`n`n## [200001.1] - 2000-01-01`n`n- Historical release.`n"
    [IO.File]::WriteAllText($changelogPath, $initial)
    $badge = '<img src="https://img.shields.io/badge/version-200001.1--dev-0A66C2" alt="Version 200001.1-dev">'
    foreach ($name in 'README.md', 'README.de.md') {
        [IO.File]::WriteAllText((Join-Path $fixture $name), "# $name`n$badge`n")
    }
    Invoke-TestGit @('add', '.')
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '-m', 'Seed release fixture',
        '-m', 'Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>') | Out-Null
    $prepare = Join-Path $fixture 'scripts\Prepare-Release.ps1'
    $updater = Join-Path $fixture 'scripts\Update-ReadmeVersion.ps1'
    $guard = Join-Path $fixture 'scripts\Assert-CommitMetadata.ps1'
    $version = & (Join-Path $fixture 'scripts\Get-ProductVersion.ps1') -NextCommit -Branch main
    Assert-Rejected { & $prepare -Verify -Version $version } '*Prepare CHANGELOG.md on dev*'
    $preparedVersion = & $prepare
    if ($preparedVersion -cne $version) { throw 'Preparation must use the next Git-derived version.' }
    $prepared = [IO.File]::ReadAllText($changelogPath)
    if (-not $prepared.Contains("## [Unreleased]`n`n## [$version]") -or
        -not $prepared.Contains('- First change.') -or -not $prepared.Contains('- Second change.') -or
        -not $prepared.Contains('## [200001.1] - 2000-01-01')) {
        throw 'Preparation must move all entries and preserve historical releases.'
    }
    if (-not [IO.File]::ReadAllText((Join-Path $fixture 'README.md')).Contains('Version 200001.1-dev')) {
        throw 'Preparation must not stamp main badges on dev.'
    }
    $notes = & $prepare -Verify -Version $version
    if (-not $notes.Contains('- First change.') -or $notes.Contains('Historical release')) {
        throw 'Release notes must contain only the prepared release.'
    }
    Assert-Rejected { & $prepare } '*already prepared*'
    Assert-Rejected { & $prepare -Finalize } "*requires 'main'*"
    [IO.File]::WriteAllText($changelogPath, $prepared.Replace('## [Unreleased]', "## [Unreleased]`n`n- Unprepared change."))
    Assert-Rejected { & $prepare -Verify -Version $version } '*Prepare CHANGELOG.md on dev*'
    [IO.File]::WriteAllText($changelogPath, $prepared.Replace('### Changed', '### Empty').Replace('- First change.', '').Replace('- Second change.', ''))
    Assert-Rejected { & $prepare -Verify -Version $version } '*Prepare CHANGELOG.md on dev*'
    [IO.File]::WriteAllText($changelogPath, $prepared)
    Assert-Rejected { & $prepare -Verify -Version '200001.1' } '*Prepare CHANGELOG.md on dev*'
    Invoke-TestGit @('add', 'CHANGELOG.md')
    & $updater -Staged | Out-Null
    & $guard -Staged | Out-Null
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '-m', 'Prepare release',
        '-m', 'Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>') | Out-Null
    Invoke-TestGit @('switch', '-c', 'main') | Out-Null
    Assert-Rejected { & $prepare } "*requires 'dev'*"
    & $prepare -Finalize | Out-Null
    Invoke-TestGit @('add', 'README.md', 'README.de.md', 'CHANGELOG.md')
    & $guard -Staged | Out-Null
    foreach ($name in 'README.md', 'README.de.md') {
        & $updater -Path (Join-Path $fixture $name) -Version $version -Verify | Out-Null
    }
    $finalized = [IO.File]::ReadAllText($changelogPath)
    & $prepare -Finalize | Out-Null
    if ([IO.File]::ReadAllText($changelogPath) -cne $finalized) {
        throw 'Repeated finalization must not duplicate release metadata entries.'
    }
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
'Release preparation tests passed.'
