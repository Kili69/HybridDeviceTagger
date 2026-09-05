#Requires -Version 7.2
# Project-Version: 0.1.20260905.6
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts and documentation, even if Microsoft has been advised of the possibility of such damages.
#>

<#
.SYNOPSIS
Displays operational data from the HybridDeviceTagger management REST API.

.DESCRIPTION
Obtains an Entra bearer token for the supplied API client ID without a client secret and
emits rules, the latest device-tagging status, or parsed latest-run JSONL trace events.
The script calls only the HybridDeviceTagger REST API and does not query Azure Resource Manager.

.PARAMETER ApiBaseUri
Base URI of the HybridDeviceTagger management API, including the /api/management path.

.PARAMETER ManagementApiClientId
Application client ID of the Entra app registration protecting the management API.

.PARAMETER ShowRules
Emits the current effective tagging rules.

.PARAMETER ShowTrace
Downloads the latest device-tagging JSONL trace in memory and emits one object per line.

.PARAMETER ShowStatus
Emits the status of the latest device-tagging run.

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ApiBaseUri 'https://func-hybriddevicetagger.azurewebsites.net/api/management' `
    -ManagementApiClientId '5e324803-5aed-4e5d-b49f-a4bfceddcda8' `
    -ShowRules

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ApiBaseUri 'https://func-hybriddevicetagger.azurewebsites.net/api/management' `
    -ManagementApiClientId '5e324803-5aed-4e5d-b49f-a4bfceddcda8' `
    -ShowTrace | Format-Table timestampUtc, level, phase, action, outcome

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -ApiBaseUri 'https://func-hybriddevicetagger.azurewebsites.net/api/management' `
    -ManagementApiClientId '5e324803-5aed-4e5d-b49f-a4bfceddcda8' `
    -ShowStatus | Format-List
#>

[CmdletBinding(DefaultParameterSetName = 'Rules')]
param(
    [Parameter(Mandatory)]
    [uri]$ApiBaseUri,

    [Parameter(Mandatory)]
    [guid]$ManagementApiClientId,

    [Parameter(Mandatory, ParameterSetName = 'Rules')]
    [switch]$ShowRules,

    [Parameter(Mandatory, ParameterSetName = 'Trace')]
    [switch]$ShowTrace,

    [Parameter(Mandatory, ParameterSetName = 'Status')]
    [switch]$ShowStatus
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not (Get-Command az -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Install Azure CLI, run az login, and try again.'
}
if (-not $ApiBaseUri.IsAbsoluteUri -or $ApiBaseUri.Scheme -ne 'https') {
    throw 'ApiBaseUri must be an absolute HTTPS URI.'
}

$tokenResponse = az account get-access-token `
    --scope "api://$ManagementApiClientId/.default" `
    --output json `
    --only-show-errors | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$tokenResponse.accessToken)) {
    throw "Unable to obtain an Entra access token for api://$ManagementApiClientId. Run 'az login --scope api://$ManagementApiClientId/.default' and try again."
}

$headers = @{ Authorization = "Bearer $($tokenResponse.accessToken)" }
$managementApiBaseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')

if ($ShowRules) {
    $configuration = Invoke-RestMethod `
        -Method Get `
        -Uri "$managementApiBaseUri/tagging-rules" `
        -Headers $headers
    Write-Verbose "Rule source: $($configuration.source); ETag: $($configuration.eTag)"
    $configuration.rules
}
elseif ($ShowStatus) {
    Invoke-RestMethod `
        -Method Get `
        -Uri "$managementApiBaseUri/status/device-tagging" `
        -Headers $headers
}
elseif ($ShowTrace) {
    $traceContent = Invoke-RestMethod `
        -Method Get `
        -Uri "$managementApiBaseUri/traces/device-tagging/latest" `
        -Headers $headers
    foreach ($line in $traceContent -split "`r?`n") {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $line | ConvertFrom-Json
        }
    }
}