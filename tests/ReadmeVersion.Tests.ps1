#Requires -Version 7.2

<#
.SYNOPSIS
Tests the README version badge update and verification without Git changes.

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
'README version tests passed.'