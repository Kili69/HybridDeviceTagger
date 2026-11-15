#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Verifies the changelog and file-header conventions before a commit.

.DESCRIPTION
The product version is calculated from Git tags at build time (see Get-ProductVersion.ps1), so a
commit no longer changes a version number. Instead, this check enforces:

- every commit that changes files other than CHANGELOG.md also updates CHANGELOG.md;
- CHANGELOG.md contains an "## [Unreleased]" section with at least one entry, or a release
  section in the format "## [<YYYY><MM>.<counter>] - <yyyy-MM-dd>";
- every changed production PowerShell file carries the source version marker
  "# Project-Version: source" and the author line, which the package build replaces with the
  product version;
- every changed production C# and Bicep file carries the author line;
- the version badge in README.md shows the version of the new commit
  (Update-ReadmeVersion.ps1, called by the pre-commit hook, updates it).

.PARAMETER Staged
Checks the staged changes instead of the working tree.

.EXAMPLE
pwsh -NoProfile -File scripts/Assert-CommitMetadata.ps1 -Staged

Validates the staged changes before git commit.

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
param(
    [switch]$Staged
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$powerShellAuthorLine = '# Author: andreas.lucas@outlook.com (aka Kili)'
$sourceAuthorLine = 'Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>'
$versionMarker = '# Project-Version: source'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repositoryRoot
try {
    $diffArguments = @('diff', '--name-only', '--diff-filter=ACMR')
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

    $failures = [System.Collections.Generic.List[string]]::new()
    $otherChanges = @($changedFiles | Where-Object { $_ -ne 'CHANGELOG.md' })
    if ($otherChanges.Count -gt 0 -and 'CHANGELOG.md' -notin $changedFiles) {
        $failures.Add('Every commit must describe its changes in CHANGELOG.md.')
    }

    $changelog = Get-Content -LiteralPath 'CHANGELOG.md'
    $unreleasedIndex = [array]::IndexOf($changelog, '## [Unreleased]')
    if ($unreleasedIndex -lt 0) {
        $failures.Add('CHANGELOG.md must contain an "## [Unreleased]" section.')
    }
    else {
        $nextSection = @($changelog[($unreleasedIndex + 1)..($changelog.Count - 1)] |
            Select-String -Pattern '^## ' | Select-Object -First 1)
        $sectionEnd = if ($nextSection.Count -gt 0) { $unreleasedIndex + $nextSection[0].LineNumber } else { $changelog.Count }
        $unreleasedEntries = @($changelog[($unreleasedIndex + 1)..($sectionEnd - 1)] | Where-Object { $_ -match '^- ' })
        $latestRelease = $nextSection.Count -gt 0 -and $nextSection[0].Line -match '^## \[\d{6}\.\d+\] - \d{4}-\d{2}-\d{2}$'
        if ($otherChanges.Count -gt 0 -and $unreleasedEntries.Count -eq 0 -and -not $latestRelease) {
            $failures.Add('Add an entry below "## [Unreleased]" or a "## [<YYYY><MM>.<counter>] - <yyyy-MM-dd>" release section.')
        }
    }

    try {
        $badgeArguments = @{ Verify = $true; Staged = [bool]$Staged }
        & (Join-Path $PSScriptRoot 'Update-ReadmeVersion.ps1') @badgeArguments | Out-Null
    }
    catch {
        $failures.Add($_.Exception.Message)
    }

    foreach ($file in $changedFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or $file -like 'tests/*') {
            continue
        }
        $header = @(Get-Content -LiteralPath $file -TotalCount 8)
        switch -Regex ($file) {
            '\.(ps1|psm1|psd1)$' {
                if ($versionMarker -notin $header) {
                    $failures.Add("$file must contain the line '$versionMarker' in its header.")
                }
                if ($powerShellAuthorLine -notin $header) {
                    $failures.Add("$file must contain the line '$powerShellAuthorLine' in its header.")
                }
            }
            '\.(cs|bicep)$' {
                if (-not ($header | Where-Object { $_.Contains($sourceAuthorLine) })) {
                    $failures.Add("$file must contain '$sourceAuthorLine' in its header.")
                }
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join [Environment]::NewLine)
    }

    Write-Output 'Commit metadata valid.'
}
finally {
    Pop-Location
}
