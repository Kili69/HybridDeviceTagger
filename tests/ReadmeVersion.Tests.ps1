#Requires -Version 7.2

<#
.SYNOPSIS
Tests README badges and paired translations using temporary files and an isolated Git repository.

.EXAMPLE
pwsh -NoProfile -File tests\ReadmeVersion.Tests.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts\Update-ReadmeVersion.ps1'
$readme = New-TemporaryFile
try {
    [IO.File]::WriteAllText($readme.FullName,
        "# Test`n<img src=`"https://img.shields.io/badge/version-YYYYMM.counter-0A66C2`" alt=`"Version YYYYMM.counter`">`nÄÖÜ`n")

    & $script -Path $readme.FullName -Version '202611.2-feature-x' | Out-Null
    $text = [IO.File]::ReadAllText($readme.FullName)
    if (-not $text.Contains('badge/version-202611.2--feature--x-0A66C2" alt="Version 202611.2-feature-x"') -or
        -not $text.Contains('ÄÖÜ')) {
        throw "The badge must show the escaped branch version and keep other content: $text"
    }

    & $script -Path $readme.FullName -Version '202611.2-feature-x' -Verify | Out-Null
    $rejected = $false
    try { & $script -Path $readme.FullName -Version '202611.2' -Verify | Out-Null } catch { $rejected = $true }
    if (-not $rejected) {
        throw 'Verify must reject a badge that shows another version.'
    }
}
finally {
    Remove-Item -LiteralPath $readme.FullName -Force
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) "HybridDeviceTagger-ReadmeTests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
try {
    foreach ($name in 'Update-ReadmeVersion.ps1', 'Assert-CommitMetadata.ps1', 'Get-ProductVersion.ps1') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts\$name") -Destination (Join-Path $fixture 'scripts')
    }

    function Invoke-TestGit {
        param([string[]]$Arguments)

        $output = & git -C $fixture @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: $($Arguments -join ' ')" }
        $output
    }

    function Assert-ReadmeGuard {
        param([bool]$ShouldPass, [switch]$Staged)

        $message = ''
        try { & $guard -Staged:$Staged | Out-Null } catch { $message = $_.Exception.Message }
        if ($ShouldPass -and $message) { throw "Expected valid paired README changes: $message" }
        if (-not $ShouldPass -and $message -notlike '*must be updated together*') {
            throw "Expected a paired-translation rejection, got: $message"
        }
    }

    Invoke-TestGit @('init', '--initial-branch=main') | Out-Null
    Invoke-TestGit @('config', 'user.name', 'README tests')
    Invoke-TestGit @('config', 'user.email', 'readme-tests@example.invalid')
    Invoke-TestGit @('config', 'core.hooksPath', '.disabled-test-hooks')
    $englishPath = Join-Path $fixture 'README.md'
    $germanPath = Join-Path $fixture 'README.de.md'
    $changelogPath = Join-Path $fixture 'CHANGELOG.md'
    $badge = '<img src="https://img.shields.io/badge/version-200001.1-0A66C2" alt="Version 200001.1">'
    $english = "# Test`n$badge`nOriginal English content.`n"
    $german = "# Test`n$badge`nUrsprünglicher deutscher Inhalt.`n"
    [IO.File]::WriteAllText($englishPath, $english)
    [IO.File]::WriteAllText($germanPath, $german)
    [IO.File]::WriteAllText($changelogPath, "# Changelog`n`n## [Unreleased]`n")
    Invoke-TestGit @('add', '--', 'README.md', 'README.de.md', 'CHANGELOG.md')
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '-m', 'Seed README test fixture',
        '-m', 'Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>') | Out-Null

    $updater = Join-Path $fixture 'scripts\Update-ReadmeVersion.ps1'
    $guard = Join-Path $fixture 'scripts\Assert-CommitMetadata.ps1'
    $version = & (Join-Path $fixture 'scripts\Get-ProductVersion.ps1') -NextCommit
    [IO.File]::WriteAllText($changelogPath, "# Changelog`n`n## [Unreleased]`n`n- Test paired translations.`n")
    [IO.File]::WriteAllText($englishPath, "$english`nEnglish change.`n")
    Invoke-TestGit @('add', '--', 'README.md', 'README.de.md', 'CHANGELOG.md')

    & $updater -Staged | Out-Null
    Assert-ReadmeGuard -ShouldPass $false -Staged
    [IO.File]::WriteAllText($germanPath, "$german`nDeutsche Änderung.`n")
    & $updater | Out-Null
    Assert-ReadmeGuard -ShouldPass $false -Staged

    [IO.File]::WriteAllText($englishPath, "$english`nEnglish change.`n")
    [IO.File]::WriteAllText($germanPath, "$german`nDeutsche Änderung.`n")
    Invoke-TestGit @('add', '--', 'README.md', 'README.de.md')
    foreach ($path in $englishPath, $germanPath) {
        [IO.File]::AppendAllText($path, "`nUNSTAGED TRANSLATION`n")
    }
    & $updater -Staged | Out-Null
    & $updater -Staged -Verify | Out-Null
    Assert-ReadmeGuard -ShouldPass $true -Staged
    foreach ($name in 'README.md', 'README.de.md') {
        $stagedText = (Invoke-TestGit @('show', ":$name")) -join "`n"
        $workingText = [IO.File]::ReadAllText((Join-Path $fixture $name))
        if ($stagedText.Contains('UNSTAGED TRANSLATION') -or
            -not $workingText.Contains('UNSTAGED TRANSLATION') -or
            -not $stagedText.Contains("alt=`"Version $version`"") -or
            -not $workingText.Contains("alt=`"Version $version`"")) {
            throw "$name badge updates must preserve unstaged content without staging it."
        }
    }
    if (-not [IO.File]::ReadAllText($germanPath).Contains('Deutsche Änderung.')) {
        throw 'Updating both badges must preserve German text.'
    }

    [IO.File]::WriteAllText($englishPath, $english)
    [IO.File]::WriteAllText($germanPath, "$german`nDeutsche Änderung.`n")
    Invoke-TestGit @('add', '--', 'README.md', 'README.de.md')
    & $updater -Staged | Out-Null
    Assert-ReadmeGuard -ShouldPass $false -Staged
    [IO.File]::AppendAllText($changelogPath, "`n- Working-tree test.`n")
    Assert-ReadmeGuard -ShouldPass $false
    [IO.File]::AppendAllText($englishPath, "`nEnglish change.`n")
    Assert-ReadmeGuard -ShouldPass $true
    Assert-ReadmeGuard -ShouldPass $false -Staged

    [IO.File]::WriteAllText($englishPath, $english)
    [IO.File]::WriteAllText($germanPath, $german)
    Invoke-TestGit @('add', '--', 'README.md', 'README.de.md')
    & $updater -Staged | Out-Null
    Assert-ReadmeGuard -ShouldPass $true -Staged

    & $updater -Path $germanPath -Version '200001.1' | Out-Null
    Invoke-TestGit @('add', '--', 'README.de.md')
    $message = ''
    try { & $updater -Staged -Verify | Out-Null } catch { $message = $_.Exception.Message }
    if ($message -notlike '*README.de.md version badge*') {
        throw "Staged verification must reject a stale German badge: $message"
    }
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
'README version and translation tests passed.'