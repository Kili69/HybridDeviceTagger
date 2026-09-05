#Requires -Version 7.2
# Project-Version: 0.1.20260905.5
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts and documentation, even if Microsoft has been advised of the possibility of such damages.
#>

<#
.SYNOPSIS
Displays operational data from the HybridDeviceTagger management REST API.

.DESCRIPTION
Validates the active Azure CLI account and subscription, discovers the Function App's
Easy Auth client ID and host name, obtains an Entra bearer token without a client secret,
and emits rules, the latest device-tagging status, or parsed latest-run JSONL trace events.

.PARAMETER FunctionAppAdministratorAccount
Expected user name of the active Azure CLI account.

.PARAMETER SubscriptionId
Subscription containing the Function App.

.PARAMETER ResourceGroupName
Resource group containing the Function App.

.PARAMETER FunctionAppName
Name of the Function App.

.PARAMETER ShowRules
Emits the current effective tagging rules.

.PARAMETER ShowTrace
Downloads the latest device-tagging JSONL trace in memory and emits one object per line.

.PARAMETER ShowStatus
Emits the status of the latest device-tagging run.

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -FunctionAppAdministratorAccount 'kili@bloedgelaber.de' `
    -SubscriptionId '23ca129f-1e1c-4a56-bc51-e3ec2c874a73' `
    -ResourceGroupName 'RG-HybridDeviceTagger' `
    -FunctionAppName 'func-hybriddevicetagger' `
    -ShowRules

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -FunctionAppAdministratorAccount 'kili@bloedgelaber.de' `
    -SubscriptionId '23ca129f-1e1c-4a56-bc51-e3ec2c874a73' `
    -ResourceGroupName 'RG-HybridDeviceTagger' `
    -FunctionAppName 'func-hybriddevicetagger' `
    -ShowTrace | Format-Table timestampUtc, level, phase, action, outcome

.EXAMPLE
./Deployment/Show-HybridDeviceTaggerApi.ps1 `
    -FunctionAppAdministratorAccount 'kili@bloedgelaber.de' `
    -SubscriptionId '23ca129f-1e1c-4a56-bc51-e3ec2c874a73' `
    -ResourceGroupName 'RG-HybridDeviceTagger' `
    -FunctionAppName 'func-hybriddevicetagger' `
    -ShowStatus | Format-List
#>

[CmdletBinding(DefaultParameterSetName = 'Rules')]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 320)]
    [string]$FunctionAppAdministratorAccount,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 90)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9-]{2,60}$')]
    [string]$FunctionAppName,

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

$account = az account show --output json --only-show-errors | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $account) {
    throw 'Unable to read the active Azure CLI account. Run az login and try again.'
}
if ([string]$account.user.name -ine $FunctionAppAdministratorAccount) {
    throw "Function App administrator account mismatch. Expected '$FunctionAppAdministratorAccount', active Azure CLI account is '$($account.user.name)'."
}
if ([string]$account.id -ine $SubscriptionId) {
    az account set --subscription $SubscriptionId --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to select subscription '$SubscriptionId'."
    }
}

$functionApp = az functionapp show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroupName `
    --name $FunctionAppName `
    --output json `
    --only-show-errors | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $functionApp) {
    throw "Function App '$FunctionAppName' was not found in resource group '$ResourceGroupName'."
}

$functionAppProperties = if ($functionApp.PSObject.Properties['properties']) {
    $functionApp.properties
}
else {
    $functionApp
}
$defaultHostName = [string]$functionAppProperties.defaultHostName
if ([string]::IsNullOrWhiteSpace($defaultHostName)) {
    throw "Function App '$FunctionAppName' does not expose a default host name."
}

$authSettingsUrl = "https://management.azure.com$($functionApp.id)/config/authsettingsV2?api-version=2022-09-01"
$authSettings = az rest `
    --method get `
    --url $authSettingsUrl `
    --output json `
    --only-show-errors | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $authSettings) {
    throw "Unable to read Easy Auth configuration from Function App '$FunctionAppName'."
}

$managementApiClientId = [string]$authSettings.properties.identityProviders.azureActiveDirectory.registration.clientId
if ($managementApiClientId -notmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    throw "Function App '$FunctionAppName' does not have a valid Entra Easy Auth client ID."
}

$tokenResponse = az account get-access-token `
    --scope "api://$managementApiClientId/.default" `
    --output json `
    --only-show-errors | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$tokenResponse.accessToken)) {
    throw "Unable to obtain an Entra access token for api://$managementApiClientId. Run 'az login --scope api://$managementApiClientId/.default' and try again."
}

$headers = @{ Authorization = "Bearer $($tokenResponse.accessToken)" }
$managementApiBaseUri = "https://$defaultHostName/api/management"

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