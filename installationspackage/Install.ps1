#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
.SYNOPSIS
Guides an operator through the complete HybridDeviceTagger installation.

.DESCRIPTION
Install.ps1 is the interactive entry point of the HybridDeviceTagger installation package. It:

1. checks the local prerequisites (PowerShell 7.2, Azure CLI, the ActiveDirectory module, the
   prebuilt Function package, and an Azure CLI sign-in);
2. asks for every installation parameter, proposing defaults and previously saved answers;
3. validates the format of every answer and asks again when a value is invalid;
4. stores the answers without any secret in installation-settings.json so that later runs, for
   example an update or a certificate change, can reuse them;
5. runs Install-HybridDeviceTagger.ps1 in the selected mode. A full installation always runs the
   read-only validation first, optionally shows the Azure What-If result, and deploys only after
   confirmation.

Passwords are never written to disk. The Active Directory service-account credential is requested
once per run and passed to the installation engine in memory only. After deployment, the engine
sends it once over HTTPS to the Function, which validates it against Active Directory and stores
it in Key Vault through its managed identity.

Use Install-HybridDeviceTagger.ps1 directly for unattended or pipeline installations.

.PARAMETER SettingsPath
Path of the JSON file that stores the answers. Defaults to installation-settings.json next to
this script.

.PARAMETER Mode
Selects the action without asking. Install runs validation, optional What-If, and installation.
Validate and WhatIf make no changes.

.PARAMETER UseSavedSettings
Accepts all saved answers without asking again. Missing answers are still requested.

.EXAMPLE
./Install.ps1

Asks for all parameters and the action, then runs it.

.EXAMPLE
./Install.ps1 -Mode Validate -UseSavedSettings

Repeats the read-only validation with the previously saved answers.

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

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The guided installer writes interactive, colored console output.'
)]
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'installation-settings.json'),

    [ValidateSet('Install', 'Validate', 'WhatIf')]
    [string]$Mode,

    [switch]$UseSavedSettings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$enginePath = Join-Path $PSScriptRoot 'Install-HybridDeviceTagger.ps1'
$guidPattern = '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
$dnsNamePattern = '^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$'
$ncrontabPattern = '^\S+(\s+\S+){5}$'

<#
.SYNOPSIS
Writes a highlighted section heading.
#>
function Write-Section {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host "`n=== $Title ===" -ForegroundColor Cyan
}

<#
.SYNOPSIS
Asks for one text value, offering a default and validating the answer.

.DESCRIPTION
Pressing Enter accepts the default. Optional values may be cleared by entering a single hyphen.
Invalid answers display the validation message and are requested again.

.PARAMETER Prompt
The question displayed to the operator.

.PARAMETER Default
The proposed value accepted by pressing Enter.

.PARAMETER Pattern
A regular expression the answer must match.

.PARAMETER ValidationMessage
The message displayed when the answer does not match Pattern.

.PARAMETER Optional
Allows an empty answer.

.PARAMETER Help
A short explanation displayed before the question.
#>
function Read-Value {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [AllowNull()][AllowEmptyString()][string]$Default,
        [string]$Pattern,
        [string]$ValidationMessage = 'The value has an invalid format.',
        [switch]$Optional,
        [string]$Help
    )

    if ($Help) {
        Write-Host "  $Help" -ForegroundColor DarkGray
    }
    while ($true) {
        $label = if ([string]::IsNullOrWhiteSpace($Default)) {
            if ($Optional) { "$Prompt (optional)" } else { $Prompt }
        }
        else {
            "$Prompt [$Default]"
        }
        $answer = (Read-Host $label).Trim()
        if ($answer -eq '-' -and $Optional) {
            return ''
        }
        if ([string]::IsNullOrWhiteSpace($answer)) {
            $answer = [string]$Default
        }
        if ([string]::IsNullOrWhiteSpace($answer)) {
            if ($Optional) {
                return ''
            }
            Write-Host '  A value is required.' -ForegroundColor Yellow
            continue
        }
        if ($Pattern -and $answer -notmatch $Pattern) {
            Write-Host "  $ValidationMessage" -ForegroundColor Yellow
            continue
        }
        return $answer
    }
}

<#
.SYNOPSIS
Asks a yes/no question and returns the answer as a Boolean.
#>
function Read-YesNo {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [bool]$Default = $false
    )

    $hint = if ($Default) { 'Y/n' } else { 'y/N' }
    while ($true) {
        $answer = (Read-Host "$Prompt [$hint]").Trim()
        if ([string]::IsNullOrWhiteSpace($answer)) {
            return $Default
        }
        switch -Regex ($answer) {
            '^(y|yes|j|ja)$' { return $true }
            '^(n|no|nein)$' { return $false }
        }
        Write-Host '  Answer with y or n.' -ForegroundColor Yellow
    }
}

<#
.SYNOPSIS
Returns a saved answer, or the supplied fallback when no answer was saved.
#>
function Get-SavedValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()]$Fallback
    )

    if ($null -ne $saved -and $saved.ContainsKey($Name) -and
        -not [string]::IsNullOrWhiteSpace([string]$saved[$Name])) {
        return $saved[$Name]
    }
    $Fallback
}

<#
.SYNOPSIS
Returns a saved answer when UseSavedSettings applies, otherwise asks for it.

.DESCRIPTION
All parameters except Name are passed to Read-Value. The saved answer becomes the default.
#>
function Get-Answer {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Prompt,
        [AllowNull()][AllowEmptyString()][string]$Default,
        [string]$Pattern,
        [string]$ValidationMessage = 'The value has an invalid format.',
        [switch]$Optional,
        [string]$Help
    )

    $effectiveDefault = [string](Get-SavedValue -Name $Name -Fallback $Default)
    if ($UseSavedSettings -and $null -ne $saved -and $saved.ContainsKey($Name) -and
        ($Optional -or -not [string]::IsNullOrWhiteSpace($effectiveDefault))) {
        Write-Host "  $Prompt`: $effectiveDefault" -ForegroundColor DarkGray
        return $effectiveDefault
    }

    Read-Value `
        -Prompt $Prompt `
        -Default $effectiveDefault `
        -Pattern $Pattern `
        -ValidationMessage $ValidationMessage `
        -Optional:$Optional `
        -Help $Help
}

<#
.SYNOPSIS
Returns the Function App name without the conventional func- prefix.
#>
function Get-NameSuffix {
    param([Parameter(Mandatory)][string]$FunctionAppName)

    $name = $FunctionAppName.ToLowerInvariant()
    if ($name.StartsWith('func-', [StringComparison]::Ordinal)) {
        return $name.Substring(5)
    }
    $name
}

<#
.SYNOPSIS
Returns the DNS name of the forest of the signed-in Windows user, or an empty string.
#>
function Get-LoggedOnForest {
    try {
        Import-Module ActiveDirectory -ErrorAction Stop -Verbose:$false
        [string](Get-ADForest -Current LoggedOnUser -ErrorAction Stop).Name
    }
    catch {
        ''
    }
}

<#
.SYNOPSIS
Verifies the local prerequisites and the Azure CLI sign-in.
#>
function Assert-Prerequisite {
    Write-Section 'Checking prerequisites'
    if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
        throw "The installation engine '$enginePath' is missing. Extract the complete installation package."
    }
    if (-not (Get-Command az -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI is required. Install Azure CLI 2.60.0 or newer and run this script again.'
    }
    if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) {
        throw 'The ActiveDirectory PowerShell module is required. Install the RSAT Active Directory tools and run this script again.'
    }
    $functionPackage = Join-Path $PSScriptRoot 'function/HybridDeviceTagger.zip'
    if (-not (Test-Path -LiteralPath $functionPackage -PathType Leaf)) {
        throw "The prebuilt Function package '$functionPackage' is missing. Use a release package or run scripts/Build-InstallationPackage.ps1 first."
    }

    $account = & az account show --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  Azure CLI is not signed in.' -ForegroundColor Yellow
        if (-not (Read-YesNo -Prompt 'Sign in with az login now?' -Default $true)) {
            throw 'An Azure CLI sign-in is required.'
        }
        & az login --only-show-errors --output none
        if ($LASTEXITCODE -ne 0) {
            throw 'Azure CLI sign-in failed.'
        }
        $account = & az account show --only-show-errors --output json
    }
    $script:AzureAccount = $account | ConvertFrom-Json
    Write-Host "  Signed in as $($script:AzureAccount.user.name) in tenant $($script:AzureAccount.tenantId)." -ForegroundColor Green
}

<#
.SYNOPSIS
Returns the object ID of the signed-in Entra user, or an empty string.
#>
function Get-SignedInUserObjectId {
    $id = & az ad signed-in-user show --query id --output tsv --only-show-errors 2>$null
    if ($LASTEXITCODE -eq 0 -and [string]$id -match $guidPattern) {
        return [string]$id
    }
    ''
}

<#
.SYNOPSIS
Loads previously saved answers from SettingsPath.
#>
function Read-SavedSetting {
    if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
        return $null
    }
    $loaded = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json -AsHashtable
    Write-Host "  Saved answers loaded from '$SettingsPath'." -ForegroundColor Green
    $loaded
}

<#
.SYNOPSIS
Collects every installation parameter and returns them as an ordered dictionary.
#>
function Read-InstallationSetting {
    $settings = [ordered]@{}

    Write-Section 'Azure target'
    $settings.SubscriptionId = Get-Answer -Name SubscriptionId `
        -Prompt 'Azure subscription ID' `
        -Default $script:AzureAccount.id `
        -Pattern $guidPattern -ValidationMessage 'Enter a subscription GUID.'
    $settings.ResourceGroupName = Get-Answer -Name ResourceGroupName `
        -Prompt 'Resource group for the HybridDeviceTagger resources' `
        -Default 'rg-HybridDeviceTagger' `
        -Pattern '^[-\w\._\(\)]{1,90}$' -ValidationMessage 'Use up to 90 letters, digits, hyphens, underscores, periods, or parentheses.'
    $settings.CreateResourceGroup = $true
    $settings.Location = Get-Answer -Name Location `
        -Prompt 'Azure region (must match the virtual network)' `
        -Default 'westeurope' `
        -Pattern '^[a-z0-9]{2,64}$' -ValidationMessage 'Use the region name, for example westeurope.'
    $settings.FunctionAppName = Get-Answer -Name FunctionAppName `
        -Prompt 'Function App name (globally unique)' `
        -Default 'func-hybriddevicetagger' `
        -Pattern '^[a-zA-Z0-9-]{2,60}$' -ValidationMessage 'Use 2-60 letters, digits, or hyphens.'

    $suffix = Get-NameSuffix -FunctionAppName $settings.FunctionAppName
    $storageSuffix = $suffix -replace '[^a-z0-9]', ''
    if ($storageSuffix.Length -gt 22) { $storageSuffix = $storageSuffix.Substring(0, 22) }
    $settings.StorageAccountName = Get-Answer -Name StorageAccountName `
        -Prompt 'Storage account name (globally unique)' `
        -Default "st$storageSuffix" `
        -Pattern '^[a-z0-9]{3,24}$' -ValidationMessage 'Use 3-24 lowercase letters and digits.'

    Write-Section 'Existing network'
    Write-Host '  The virtual network and both subnets must already exist. Private DNS must resolve the' -ForegroundColor DarkGray
    Write-Host '  privatelink.blob and privatelink.queue zones from the virtual network.' -ForegroundColor DarkGray
    $settings.VirtualNetworkSubscriptionId = Get-Answer -Name VirtualNetworkSubscriptionId `
        -Prompt 'Virtual network subscription ID' `
        -Default $settings.SubscriptionId `
        -Pattern $guidPattern -ValidationMessage 'Enter a subscription GUID.'
    $settings.VirtualNetworkResourceGroupName = Get-Answer -Name VirtualNetworkResourceGroupName `
        -Prompt 'Virtual network resource group'
    $settings.VirtualNetworkName = Get-Answer -Name VirtualNetworkName -Prompt 'Virtual network name'
    $settings.SubnetName = Get-Answer -Name SubnetName `
        -Prompt 'Integration subnet (delegated to Microsoft.App/environments)'
    $settings.PrivateEndpointSubnetName = Get-Answer -Name PrivateEndpointSubnetName `
        -Prompt 'Private endpoint subnet'

    Write-Section 'Key Vault'
    $savedCreateKeyVault = [bool](Get-SavedValue -Name CreateKeyVault -Fallback $false)
    $settings.CreateKeyVault = if ($UseSavedSettings -and $null -ne $saved -and $saved.ContainsKey('CreateKeyVault')) {
        $savedCreateKeyVault
    }
    else {
        Read-YesNo `
            -Prompt 'Create a new private Key Vault for HybridDeviceTagger? (No uses an existing vault)' `
            -Default $savedCreateKeyVault
    }
    if ($settings.CreateKeyVault) {
        $settings.KeyVaultSubscriptionId = $settings.SubscriptionId
        $settings.KeyVaultResourceGroupName = $settings.ResourceGroupName
    }
    else {
        Write-Host '  The existing vault must use Azure RBAC, soft delete, and purge protection.' -ForegroundColor DarkGray
        $settings.KeyVaultSubscriptionId = Get-Answer -Name KeyVaultSubscriptionId `
            -Prompt 'Key Vault subscription ID' `
            -Default $settings.SubscriptionId `
            -Pattern $guidPattern -ValidationMessage 'Enter a subscription GUID.'
        $settings.KeyVaultResourceGroupName = Get-Answer -Name KeyVaultResourceGroupName `
            -Prompt 'Key Vault resource group' `
            -Default $settings.ResourceGroupName
    }
    $keyVaultSuffix = ($suffix -replace '[^a-z0-9-]', '-').Trim('-')
    if ($keyVaultSuffix.Length -gt 21) { $keyVaultSuffix = $keyVaultSuffix.Substring(0, 21).TrimEnd('-') }
    $settings.KeyVaultName = Get-Answer -Name KeyVaultName `
        -Prompt 'Key Vault name' `
        -Default "kv-$keyVaultSuffix" `
        -Pattern '^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$' -ValidationMessage 'Use 3-24 letters, digits, or hyphens, starting with a letter.'

    Write-Section 'Active Directory'
    $settings.ActiveDirectoryForestDomain = Get-Answer -Name ActiveDirectoryForestDomain `
        -Prompt 'Active Directory forest DNS name' `
        -Default (Get-LoggedOnForest) `
        -Pattern $dnsNamePattern -ValidationMessage 'Enter a DNS name, for example contoso.com.'
    $settings.ActiveDirectoryServer = Get-Answer -Name ActiveDirectoryServer `
        -Prompt 'Domain controller for installer validation' `
        -Optional `
        -Pattern $dnsNamePattern -ValidationMessage 'Enter a DNS name.' `
        -Help 'Leave empty to discover a domain controller through DNS SRV records. The Function always uses DNS SRV discovery.'
    $settings.ServiceAccountUpn = Get-Answer -Name ServiceAccountUpn `
        -Prompt 'Service account UPN' `
        -Default "HybridDeviceTagger@$($settings.ActiveDirectoryForestDomain)" `
        -Pattern '^[^@\s]+@[^@\s]+$' -ValidationMessage 'Enter a user principal name, for example HybridDeviceTagger@contoso.com.' `
        -Help 'The account must exist, must not have to change its password at next logon, and needs SELF / Change Password.'
    $settings.ActiveDirectoryBindUserDistinguishedName = Get-Answer -Name ActiveDirectoryBindUserDistinguishedName `
        -Prompt 'Service account distinguished name' `
        -Optional `
        -Help 'Leave empty to resolve it from the UPN.'
    $settings.ActiveDirectoryTrustedCertificatePath = Get-Answer -Name ActiveDirectoryTrustedCertificatePath `
        -Prompt 'Path to the LDAPS root CA certificate (.cer/.pem)' `
        -Optional `
        -Help 'Required when the domain controller certificate is issued by a private CA. Enter - to clear a saved value.'
    if ($settings.ActiveDirectoryTrustedCertificatePath) {
        $settings.ActiveDirectoryTrustedCertificatePath = (Resolve-Path -LiteralPath $settings.ActiveDirectoryTrustedCertificatePath).Path
    }

    Write-Section 'Management API'
    $settings.ManagementApiClientId = Get-Answer -Name ManagementApiClientId `
        -Prompt 'Application (client) ID of the management API app registration' `
        -Optional `
        -Pattern $guidPattern -ValidationMessage 'Enter the application (client) ID GUID.' `
        -Help 'Leave empty to let the installer create the app registration, or to reuse the one of an existing installation.'
    $settings.ManagementApiAllowedPrincipalObjectId = Get-Answer -Name ManagementApiAllowedPrincipalObjectId `
        -Prompt 'Object ID of the user or group that receives all management API roles' `
        -Default (Get-SignedInUserObjectId) `
        -Pattern $guidPattern -ValidationMessage 'Enter an object ID GUID.'

    Write-Section 'Operation'
    $settings.TaggingSchedule = Get-Answer -Name TaggingSchedule `
        -Prompt 'Device-tagging schedule (NCRONTAB, UTC)' `
        -Default '0 */15 * * * *' `
        -Pattern $ncrontabPattern -ValidationMessage 'Enter six NCRONTAB fields.'
    $settings.RotationSchedule = Get-Answer -Name RotationSchedule `
        -Prompt 'Password-rotation check schedule (NCRONTAB, UTC)' `
        -Default '0 30 2 * * *' `
        -Pattern $ncrontabPattern -ValidationMessage 'Enter six NCRONTAB fields.'
    $settings.RegisterProviders = [bool](Get-SavedValue -Name RegisterProviders -Fallback $true)
    if (-not $UseSavedSettings) {
        $settings.RegisterProviders = Read-YesNo `
            -Prompt 'Register missing Azure resource providers automatically?' `
            -Default $settings.RegisterProviders
    }

    $settings
}

<#
.SYNOPSIS
Writes the answers to SettingsPath. The file never contains a secret.
#>
function Save-InstallationSetting {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Settings)

    $Settings | ConvertTo-Json | Set-Content -LiteralPath $SettingsPath -Encoding utf8
    Write-Host "  Answers saved to '$SettingsPath'." -ForegroundColor Green
}

<#
.SYNOPSIS
Converts the answers to the parameter set of Install-HybridDeviceTagger.ps1.
#>
function ConvertTo-EngineParameter {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Settings
    )

    # ServiceAccountUpn is only the default user name of the credential prompt; the engine derives
    # the UPN from ADServiceAccountCredential, which cannot be combined with ADServiceAccount.
    $parameters = @{}
    foreach ($entry in $Settings.GetEnumerator()) {
        if ($entry.Key -eq 'ServiceAccountUpn') {
            continue
        }
        if ($entry.Value -is [bool]) {
            # Passing false explicitly keeps the engine from asking again, for example CreateKeyVault.
            $parameters[$entry.Key] = [switch]$entry.Value
            continue
        }
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
            continue
        }
        $parameters[$entry.Key] = $entry.Value
    }
    $parameters.ManagementApiAllowedPrincipalObjectId = @([guid]$Settings.ManagementApiAllowedPrincipalObjectId)
    $parameters.ADServiceAccountCredential = $script:ActiveDirectoryCredential
    $parameters
}

<#
.SYNOPSIS
Runs the installation engine and returns whether it succeeded.
#>
function Invoke-Engine {
    param(
        [Parameter(Mandatory)][hashtable]$Parameters,
        [Parameter(Mandatory)][string]$Description
    )

    Write-Section $Description
    try {
        & $enginePath @Parameters
        $succeeded = $?
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        $succeeded = $false
    }
    if (-not $succeeded) {
        Write-Host "$Description failed. Review the messages above and the installer log." -ForegroundColor Red
    }
    $succeeded
}

<#
.SYNOPSIS
Asks which installation action to run.
#>
function Read-Mode {
    Write-Section 'Action'
    Write-Host '  1  Install or update (validation, optional What-If, installation)'
    Write-Host '  2  Validate only (no changes)'
    Write-Host '  3  Show Azure What-If only (no changes)'
    $choice = Read-Value -Prompt 'Selection' -Default '1' -Pattern '^[1-3]$' -ValidationMessage 'Enter 1, 2, or 3.'
    @{ '1' = 'Install'; '2' = 'Validate'; '3' = 'WhatIf' }[$choice]
}

Write-Host 'HybridDeviceTagger guided installation' -ForegroundColor Cyan
$versionFile = Join-Path $PSScriptRoot 'version.json'
if (Test-Path -LiteralPath $versionFile -PathType Leaf) {
    $packageVersion = (Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json).productVersion
    Write-Host "Package version: $packageVersion"
}

Assert-Prerequisite
$saved = Read-SavedSetting
if ($UseSavedSettings -and $null -eq $saved) {
    throw "UseSavedSettings requires the settings file '$SettingsPath'."
}

$settings = Read-InstallationSetting
Save-InstallationSetting -Settings $settings

if (-not $Mode) {
    $Mode = Read-Mode
}

Write-Host "`nThe service-account password validates the LDAPS bind and is handed to the Function for Key Vault storage; it is never written to disk." -ForegroundColor DarkGray
$script:ActiveDirectoryCredential = Get-Credential `
    -UserName $settings.ServiceAccountUpn `
    -Message 'Active Directory service-account password'
if ($null -eq $script:ActiveDirectoryCredential) {
    throw 'Credential entry was canceled.'
}

switch ($Mode) {
    'Validate' {
        $parameters = ConvertTo-EngineParameter -Settings $settings
        $null = Invoke-Engine -Parameters ($parameters + @{ ValidateOnly = $true }) -Description 'Validation'
    }
    'WhatIf' {
        $parameters = ConvertTo-EngineParameter -Settings $settings
        $null = Invoke-Engine -Parameters ($parameters + @{ WhatIf = $true }) -Description 'Azure What-If'
    }
    'Install' {
        $parameters = ConvertTo-EngineParameter -Settings $settings
        if (-not (Invoke-Engine -Parameters ($parameters + @{ ValidateOnly = $true }) -Description 'Validation')) {
            return
        }
        if (Read-YesNo -Prompt 'Show the Azure What-If result before installing?' -Default $true) {
            if (-not (Invoke-Engine -Parameters ($parameters + @{ WhatIf = $true }) -Description 'Azure What-If')) {
                return
            }
        }
        if (-not (Read-YesNo -Prompt 'Start the installation now?' -Default $false)) {
            Write-Host 'Installation not started. No changes were made.' -ForegroundColor Yellow
            return
        }
        # The engine displays its change summary and requests the final confirmation itself.
        if (Invoke-Engine -Parameters $parameters -Description 'Installation') {
            Write-Host "`nNext step: add tagging rules with the HybridDeviceTagger.Management module as shown above." -ForegroundColor Green
        }
    }
}
