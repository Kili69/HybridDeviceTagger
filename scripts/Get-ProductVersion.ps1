#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Calculates the HybridDeviceTagger product version from Git tags and the current branch.

.DESCRIPTION
The product version uses the format <YYYY><MM>.<counter>. Release tags use the format
v<YYYY><MM>.<counter> and are created only from the main branch.

- When HEAD carries a release tag, the version is taken from that tag.
- Otherwise the counter is the highest counter of the release tags of the current UTC month plus
  one, or 1 when the month has no release yet.
- When the current branch is not main, the sanitized branch name is appended as a suffix, for
  example 202611.3-feature-new-installer.

In a detached HEAD, for example in a CI checkout, the branch name is taken from the
GITHUB_HEAD_REF, GITHUB_REF_NAME, or BUILD_SOURCEBRANCHNAME environment variable. When none is
available, the suffix is "detached".

.PARAMETER Date
The date whose year and month form the version prefix. Defaults to the current UTC date.

.PARAMETER Branch
Overrides the detected branch name.

.PARAMETER Detailed
Returns an object with all version components instead of the product version string.

.OUTPUTS
System.String, or a PSCustomObject when Detailed is specified.

.EXAMPLE
./scripts/Get-ProductVersion.ps1

Returns the version of the current checkout, for example 202611.1-dev.

.EXAMPLE
./scripts/Get-ProductVersion.ps1 -Detailed

Returns the product, assembly, and module versions and whether HEAD is a tagged release.

.NOTES
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

[CmdletBinding()]
[OutputType([string], [pscustomobject])]
param(
    [datetime]$Date = [datetime]::UtcNow,

    [string]$Branch,

    [switch]$Detailed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$mainBranchName = 'main'
$releaseTagPattern = '^v(?<yearMonth>\d{6})\.(?<counter>\d+)$'

<#
.SYNOPSIS
Runs a Git command in the repository and returns its standard output lines.
#>
function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git -C $repositoryRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    @($output)
}

<#
.SYNOPSIS
Returns the current branch name, falling back to CI variables in a detached HEAD.
#>
function Get-CurrentBranch {
    $currentBranch = (Invoke-Git -Arguments @('branch', '--show-current')) -join ''
    if (-not [string]::IsNullOrWhiteSpace($currentBranch)) {
        return $currentBranch.Trim()
    }

    foreach ($variableName in 'GITHUB_HEAD_REF', 'GITHUB_REF_NAME', 'BUILD_SOURCEBRANCHNAME') {
        $value = [Environment]::GetEnvironmentVariable($variableName)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return ($value.Trim() -replace '^refs/heads/', '')
        }
    }

    'detached'
}

<#
.SYNOPSIS
Converts a branch name to a version suffix containing only letters, digits, and hyphens.
#>
function ConvertTo-VersionSuffix {
    param([Parameter(Mandatory)][string]$Name)

    $suffix = ($Name -replace '[^0-9A-Za-z-]+', '-' -replace '-{2,}', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($suffix)) {
        return 'branch'
    }
    $suffix
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$headTags = @(Invoke-Git -Arguments @('tag', '--points-at', 'HEAD') |
    Where-Object { $_ -match $releaseTagPattern })
if ($headTags.Count -gt 0) {
    # Prefer the newest release tag when HEAD carries several.
    $releaseTag = $headTags |
        Sort-Object { [long]([regex]::Match($_, $releaseTagPattern).Groups['yearMonth'].Value) },
            { [long]([regex]::Match($_, $releaseTagPattern).Groups['counter'].Value) } |
        Select-Object -Last 1
    $match = [regex]::Match($releaseTag, $releaseTagPattern)
    $yearMonth = $match.Groups['yearMonth'].Value
    $counter = [int]$match.Groups['counter'].Value
    $isTaggedRelease = $true
}
else {
    $yearMonth = $Date.ToString('yyyyMM', [Globalization.CultureInfo]::InvariantCulture)
    $monthCounters = @(Invoke-Git -Arguments @('tag', '--list', "v$yearMonth.*") |
        ForEach-Object { [regex]::Match($_, $releaseTagPattern) } |
        Where-Object { $_.Success -and $_.Groups['yearMonth'].Value -eq $yearMonth } |
        ForEach-Object { [int]$_.Groups['counter'].Value })
    $counter = if ($monthCounters.Count -gt 0) {
        ($monthCounters | Measure-Object -Maximum).Maximum + 1
    }
    else {
        1
    }
    $isTaggedRelease = $false
}

$branchName = if ([string]::IsNullOrWhiteSpace($Branch)) { Get-CurrentBranch } else { $Branch.Trim() }
$suffix = if ($branchName -ceq $mainBranchName) { '' } else { ConvertTo-VersionSuffix -Name $branchName }
$coreVersion = "$yearMonth.$counter"
$productVersion = if ($suffix) { "$coreVersion-$suffix" } else { $coreVersion }

if (-not $Detailed) {
    return $productVersion
}

[pscustomobject]@{
    ProductVersion = $productVersion
    CoreVersion = $coreVersion
    YearMonth = $yearMonth
    Counter = $counter
    Branch = $branchName
    Suffix = $suffix
    # .NET assembly version parts are limited to 65535, so <YYYY><MM> is split into two parts.
    AssemblyVersion = '{0}.{1}.{2}.0' -f $yearMonth.Substring(0, 4), [int]$yearMonth.Substring(4, 2), $counter
    # PowerShell prerelease labels allow only letters and digits.
    ModulePrerelease = $suffix -replace '[^0-9A-Za-z]', ''
    ReleaseTag = "v$coreVersion"
    IsTaggedRelease = $isTaggedRelease
}
