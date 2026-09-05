#Requires -Version 7.2
# Project-Version: 0.1.20260905.14
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts and documentation, even if Microsoft has been advised of the possibility of such damages.
#>

<#
.SYNOPSIS
Displays operational data from the HybridDeviceTagger management REST API.

.DESCRIPTION
Uses the existing Azure CLI sign-in to obtain an Entra bearer token without a client secret and
emits rules, the latest device-tagging status, or parsed latest-run JSONL trace events.
The script calls only the HybridDeviceTagger REST API and does not query Azure Resource Manager.
The production API URL and token audience are built in.

.PARAMETER ApiBaseUri
Optional base URI of the HybridDeviceTagger management API, including the /api/management path.

.PARAMETER ShowRules
Emits the current effective tagging rules.

.PARAMETER ShowTrace
Downloads the latest device-tagging JSONL trace in memory and emits one line per event in the
format <time>,<severity>,<phase>,<status>,<action>,<details>.

.PARAMETER ShowLastRunStatus
Emits the status of the latest device-tagging run.

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ShowRules

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ShowTrace

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ShowLastRunStatus | Format-List
#>

[CmdletBinding(DefaultParameterSetName = 'Rules')]
param(
    [uri]$ApiBaseUri = 'https://func-hybriddevicetagger.azurewebsites.net/api/management',

    [Parameter(Mandatory, ParameterSetName = 'Rules')]
    [switch]$ShowRules,

    [Parameter(Mandatory, ParameterSetName = 'Trace')]
    [switch]$ShowTrace,

    [Parameter(Mandatory, ParameterSetName = 'LastRunStatus')]
    [Alias('ShowStatus')]
    [switch]$ShowLastRunStatus
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module `
    (Join-Path $PSScriptRoot 'HybridDeviceTagger.Management/HybridDeviceTagger.Management.psd1') `
    -Force `
    -ErrorAction Stop
Connect-HybridDeviceTagger -ApiBaseUri $ApiBaseUri

if ($ShowRules) {
    Get-HybridDeviceTaggerRule
}
elseif ($ShowLastRunStatus) {
    Get-HybridDeviceTaggerLastRunStatus
}
elseif ($ShowTrace) {
    Get-HybridDeviceTaggerTrace
}
