#Requires -Version 7.2
#Requires -Modules ActiveDirectory
# Project-Version: 0.1.20260905.11
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
.SYNOPSIS
Validates and deploys HybridDeviceTagger to an Azure Functions Flex Consumption environment.

.DESCRIPTION
Validates local prerequisites, the selected Azure account, existing networking and Key Vault
resources, Active Directory connectivity, tagging rules, and the Bicep deployment. Depending on
the selected switches, the script can prepare the Active Directory service account, run a
read-only validation, show Azure What-If output, deploy infrastructure, assign Microsoft Graph
application roles, and publish the .NET Function App.

The installer references existing virtual networks, subnets, and Key Vaults without creating or
modifying those shared resources. It uses the supplied Active Directory credentials only in the
local PowerShell process and never sends the password to Azure Resource Manager. Restricted
management administrative-unit role assignments require separate explicit approval.

Before any Azure resource, permission, Active Directory account, Key Vault secret, or application
code is changed, the installer displays a complete action summary and requests standard PowerShell
confirmation. Use -Confirm to force the prompt or -Confirm:$false for approved unattended runs.

Every invocation replaces HybridDeviceTagger-Install.log in the current user's temporary
directory. The transcript records the complete console workflow and is closed on success,
validation failure, cancellation, or an early operational return.

An empty required value or an unsuitable FC1 integration subnet entered interactively is requested
again. A missing parameter-selected VNet resource group, VNet, or subnet, an unsuitable
parameter-selected integration subnet, or a VNet region mismatch displays only the actionable
exception message by default. Use -Verbose to include the full PowerShell error record with source
location and stack information. Unexpected operational and deployment failures remain terminating
errors in both modes.

When required values are omitted during an interactive run, the installer asks for the Azure
subscription first and then collects the remaining values in installation order.

.PARAMETER SubscriptionId
Optional Azure subscription that owns the Function App deployment. When omitted during an interactive
run, the installer proposes the active Azure CLI subscription, which can be accepted by pressing Enter.

.PARAMETER ResourceGroupName
Target resource group for app-owned Azure resources. Interactive runs default to
rg-HybridDeviceTagger when the parameter is omitted.

.PARAMETER Location
Azure region for newly created resources. It must support FC1 and match the integration subnet.
Interactive runs default to westeurope when the parameter is omitted.

.PARAMETER FunctionAppName
Globally unique Function App name. Interactive runs default to func-devtagger-yyyyMMdd using the
current UTC date when the parameter is omitted.

.PARAMETER StorageAccountName
Globally unique storage account name used for Functions storage, deployment packages, and the
membership ownership ledger. Interactive runs derive the default from FunctionAppName, using an
st prefix and removing characters that Azure Storage names do not support.

.PARAMETER VirtualNetworkName
Name of the existing virtual network used by the Function App.

.PARAMETER SubnetName
Name of the existing subnet delegated for Flex Consumption integration.

.PARAMETER PrivateEndpointSubnetName
Name of the existing subnet used for the storage private endpoint.

.PARAMETER VirtualNetworkResourceGroupName
Resource group containing the existing virtual network.

.PARAMETER KeyVaultName
Name of the existing Key Vault that stores the current and staged AD passwords. Interactive runs
derive the default from FunctionAppName using a kv prefix and the Function App name suffix.

.PARAMETER KeyVaultResourceGroupName
Resource group containing the existing Key Vault. Interactive runs default to ResourceGroupName.

.PARAMETER ActiveDirectoryForestDomain
DNS name of the Active Directory forest. Interactive runs propose the forest of the logged-on
Active Directory user when it can be detected. Workgroup systems have no default.

.PARAMETER ActiveDirectoryServer
Optional DNS name of the domain controller used for validation and initial forest discovery.
Defaults to ActiveDirectoryForestDomain for DNS-based discovery.

.PARAMETER AllowLdapFallback
Allows signed and sealed LDAP on the fallback port when strict LDAPS is unavailable. Password
changes still require LDAPS.

.PARAMETER LdapFallbackPort
LDAP fallback port. The default is 389.

.PARAMETER ActiveDirectoryTrustedCertificatePath
Optional path to a public root CA or self-signed lab DC certificate used as the LDAPS trust anchor.
Private keys are rejected and never packaged.

.PARAMETER ADServiceAccount
UPN used by the Function App to bind to Active Directory. Interactive runs default to
HybridDeviceTagger@ActiveDirectoryForestDomain.

.PARAMETER ActiveDirectoryBindUserDistinguishedName
Distinguished name of the AD service account used for password changes.

.PARAMETER ActiveDirectoryCredential
Credential used locally to validate directory bind and read access. Interactive runs request the
AD service-account password when the credential is omitted and directory checks are enabled.

.PARAMETER ActiveDirectoryProvisioningCredential
Separate delegated credential used only when preparing or resetting the AD service account.

.PARAMETER VirtualNetworkSubscriptionId
Subscription containing the existing virtual network. Interactive runs propose SubscriptionId,
which can be accepted by pressing Enter.

.PARAMETER KeyVaultSubscriptionId
Subscription containing the existing Key Vault. Defaults to SubscriptionId.

.PARAMETER CurrentCredentialEntryName
Name of the Key Vault secret containing the active AD password.

.PARAMETER StagedCredentialEntryName
Name of the Key Vault secret used while rotating or recovering the AD password.

.PARAMETER TaggingRules
One or more rules containing Name, Description, BaseDn, and at least one extension-attribute,
Entra-group, or administrative-unit action.

.PARAMETER TaggingSchedule
Six-field NCRONTAB schedule for device reconciliation. The default runs every 15 minutes.

.PARAMETER RotationSchedule
Six-field NCRONTAB schedule for password-rotation checks. The default runs daily at 02:30 UTC.

.PARAMETER MaximumInstanceCount
Maximum Flex Consumption scale-out instance count.

.PARAMETER InstanceMemoryMB
Memory allocated to each FC1 instance. Valid values are 512, 2048, and 4096 MB.

.PARAMETER InitialPasswordLength
Length of a generated password when preparing or resetting the AD service account.

.PARAMETER InitialSecretExpiryDays
Lifetime in days assigned to the initial Key Vault password secret.

.PARAMETER Tags
Azure resource tags applied to app-owned resources.

.PARAMETER CreateResourceGroup
Creates the target resource group when it does not already exist.

.PARAMETER RegisterProviders
Registers missing Azure resource providers required by the deployment.

.PARAMETER PrepareActiveDirectoryAccountOnly
Creates or resets the AD service account, writes the current password secret, validates access,
and exits before Azure infrastructure or code deployment.

.PARAMETER SkipActiveDirectoryDirectoryChecks
Skips repeated forest, OU, and service-account directory checks for an existing installation.
Strict endpoint and certificate validation still runs.

.PARAMETER ValidateOnly
Runs all supported prerequisite and configuration validation without changing resources.

.PARAMETER SkipCodeDeployment
Deploys or validates infrastructure and permissions without publishing Function App code.

.PARAMETER GrantRestrictedAdministrativeUnitRole
Explicitly approves creation and assignment of the least-privilege custom role at every configured
restricted management administrative-unit scope.

.EXAMPLE
$credential = Get-Credential 'svc-HybridDeviceTagger@contoso.com'
./Deployment/Install-HybridDeviceTagger.ps1 @parameters `
    -ActiveDirectoryCredential $credential `
    -ValidateOnly

Runs the complete read-only preflight using values collected in the parameters splat.

.EXAMPLE
./Deployment/Install-HybridDeviceTagger.ps1 @parameters -Confirm:$false

Deploys the validated infrastructure and Function package. Run ValidateOnly and WhatIf first.

.NOTES
Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>

Installer log: $env:TEMP\HybridDeviceTagger-Install.log

DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The installer uses colored host output for interactive progress and completion status.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter',
    '',
    Justification = 'Top-level script parameters are consumed by installer helper functions.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingPlainTextForPassword',
    '',
    Justification = 'These string parameters contain Key Vault entry names, never credential values.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSUseShouldProcessForStateChangingFunctions',
    '',
    Justification = 'Internal mutations are guarded by the script-level ShouldProcess approval and skipped for ValidateOnly and WhatIf.'
)]
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 90)]
    [string]$ResourceGroupName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 64)]
    [string]$Location,

    [ValidatePattern('^[a-zA-Z0-9-]{2,60}$')]
    [string]$FunctionAppName,

    [ValidatePattern('^[a-z0-9]{3,24}$')]
    [string]$StorageAccountName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 64)]
    [string]$VirtualNetworkName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 80)]
    [string]$SubnetName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 80)]
    [string]$PrivateEndpointSubnetName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 90)]
    [string]$VirtualNetworkResourceGroupName,

    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$')]
    [string]$KeyVaultName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 90)]
    [string]$KeyVaultResourceGroupName,

    [ValidatePattern('^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$')]
    [string]$ActiveDirectoryForestDomain,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 253)]
    [string]$ActiveDirectoryServer,

    [switch]$AllowLdapFallback,

    [ValidateRange(1, 65535)]
    [int]$LdapFallbackPort = 389,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 4096)]
    [string]$ActiveDirectoryTrustedCertificatePath,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 256)]
    [string]$ADServiceAccount,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 2048)]
    [string]$ActiveDirectoryBindUserDistinguishedName,

    [ValidateNotNull()]
    [pscredential]$ActiveDirectoryCredential,

    [ValidateNotNull()]
    [pscredential]$ActiveDirectoryProvisioningCredential,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$ManagementApiClientId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiAllowedPrincipalObjectId,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$VirtualNetworkSubscriptionId,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$KeyVaultSubscriptionId = $SubscriptionId,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 127)]
    [string]$CurrentCredentialEntryName = 'hybrid-device-tagger-ad-password-current',

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 127)]
    [string]$StagedCredentialEntryName = 'hybrid-device-tagger-ad-password-next',

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [object[]]$TaggingRules,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 100)]
    [string]$TaggingSchedule = '0 */15 * * * *',

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 100)]
    [string]$RotationSchedule = '0 30 2 * * *',

    [ValidateRange(1, 1000)]
    [int]$MaximumInstanceCount = 10,

    [ValidateSet(512, 2048, 4096)]
    [int]$InstanceMemoryMB = 2048,

    [ValidateRange(4, 256)]
    [int]$InitialPasswordLength = 40,

    [ValidateRange(26, 365)]
    [int]$InitialSecretExpiryDays = 35,

    [ValidateCount(0, 50)]
    [hashtable]$Tags = @{},

    [switch]$CreateResourceGroup,
    [switch]$RegisterProviders,
    [switch]$PrepareActiveDirectoryAccountOnly,
    [switch]$SkipActiveDirectoryDirectoryChecks,
    [switch]$ValidateOnly,
    [switch]$SkipCodeDeployment,
    [switch]$GrantRestrictedAdministrativeUnitRole
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:TrustedCertificate = $null
$script:CompactInstallerError = $false
$virtualNetworkResourceGroupWasSupplied = $PSBoundParameters.ContainsKey(
    'VirtualNetworkResourceGroupName'
)
$virtualNetworkSubscriptionWasSupplied = $PSBoundParameters.ContainsKey(
    'VirtualNetworkSubscriptionId'
)
$virtualNetworkNameWasSupplied = $PSBoundParameters.ContainsKey('VirtualNetworkName')
$subnetNameWasSupplied = $PSBoundParameters.ContainsKey('SubnetName')
$privateEndpointSubnetNameWasSupplied = $PSBoundParameters.ContainsKey(
    'PrivateEndpointSubnetName'
)

$scriptRoot = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $scriptRoot
$templatePath = Join-Path $scriptRoot 'main.bicep'
$projectPath = Join-Path $repositoryRoot 'src/HybridDeviceTagger/HybridDeviceTagger.csproj'
$trustedCertificatePackagePath = 'certificates/active-directory-trust-anchor.pem'
$activeDirectoryServiceAccountDescription = 'Service account used by HybridDeviceTagger for Active Directory device discovery and password rotation.'
$requiredProviders = @(
    'Microsoft.App',
    'Microsoft.Authorization',
    'Microsoft.Insights',
    'Microsoft.KeyVault',
    'Microsoft.Network',
    'Microsoft.OperationalInsights',
    'Microsoft.Storage',
    'Microsoft.Web'
)

$certificateAssemblies = @(
    ([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES') -split [IO.Path]::PathSeparator)
    [System.DirectoryServices.Protocols.LdapConnection].Assembly.Location
) | Select-Object -Unique
if (-not ('HybridDeviceTaggerLdapCertificateValidatorV2' -as [type])) {
    Add-Type -ReferencedAssemblies $certificateAssemblies -TypeDefinition @'
using System;
using System.DirectoryServices.Protocols;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable SYSLIB0057

public static class HybridDeviceTaggerLdapCertificateValidatorV2
{
    public static VerifyServerCertificateCallback Create(
        string expectedServer,
        X509Certificate2 trustAnchor)
    {
        return (_, certificate) => Validate(expectedServer, certificate, trustAnchor);
    }

    public static bool ValidateTlsEndpoint(
        string expectedServer,
        int port,
        X509Certificate2 trustAnchor)
    {
        using var client = new TcpClient();
        client.Connect(expectedServer, port);
        using var stream = new SslStream(
            client.GetStream(),
            false,
            (_, certificate, _, _) =>
                certificate is not null && Validate(expectedServer, certificate, trustAnchor));
        stream.AuthenticateAsClient(expectedServer);
        return stream.IsAuthenticated && stream.IsEncrypted;
    }

    private static bool Validate(
        string expectedServer,
        X509Certificate certificate,
        X509Certificate2 trustAnchor)
    {
        using var serverCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Cert));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustAnchor);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));

        var certificateName = serverCertificate.GetNameInfo(X509NameType.DnsName, false);
        return certificateName.Equals(expectedServer, StringComparison.OrdinalIgnoreCase) &&
            chain.Build(serverCertificate);
    }
}
'@
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Get-AppResourceNameSuffix {
    $normalizedFunctionAppName = $FunctionAppName.ToLowerInvariant()
    if ($normalizedFunctionAppName.StartsWith('func-', [StringComparison]::Ordinal)) {
        return $normalizedFunctionAppName.Substring(5)
    }
    $normalizedFunctionAppName
}

function Write-InstallationSummary {
    param([Parameter()][object[]]$RestrictedAdministrativeUnits = @())

    Write-Step 'Installation summary'
    $mode = if ($ValidateOnly) {
        'Validation only (no changes)'
    }
    elseif ($WhatIfPreference) {
        'ARM What-If (no changes)'
    }
    elseif ($PrepareActiveDirectoryAccountOnly) {
        'Active Directory service-account preparation'
    }
    else {
        'Azure infrastructure and Function App deployment'
    }

    Write-Host "Mode: $mode"
    Write-Host "Azure subscription: $SubscriptionId"
    Write-Host "Target resource group: $ResourceGroupName"
    Write-Host "Azure location: $Location"

    if ($PrepareActiveDirectoryAccountOnly) {
        Write-Host 'Planned changes:'
        Write-Host "  - Create or reset AD account: $ActiveDirectoryBindUserDistinguishedName"
        Write-Host "  - Update Key Vault secret: $KeyVaultName/$CurrentCredentialEntryName"
        Write-Host '  - Azure infrastructure and Function App code will not be deployed.'
        return
    }

    Write-Host 'Planned Azure resources to create or update:'
    Write-Host "  - Function App: $FunctionAppName (.NET 8 isolated, Flex Consumption FC1)"
    Write-Host "  - Storage account: $StorageAccountName with deployment, membership-ledger, and run-traces containers"
    Write-Host "  - Flex Consumption plan: $FunctionAppName-plan"
    Write-Host "  - Log Analytics workspace: $FunctionAppName-law"
    Write-Host "  - Application Insights: $FunctionAppName-appi"
    Write-Host "  - Storage private endpoint: pe-$StorageAccountName-blob"
    Write-Host "  - Entra-authenticated management API: api://$ManagementApiClientId"
    Write-Host '  - Managed-identity role assignments for Storage and the existing Key Vault'
    Write-Host '  - Microsoft Graph application permissions for device, group, and administrative-unit management'
    if ($RestrictedAdministrativeUnits.Count -gt 0) {
        Write-Host "  - Restricted administrative-unit role assignments: $($RestrictedAdministrativeUnits.Count)"
    }
    if ($RegisterProviders) {
        Write-Host '  - Register required Azure resource providers when missing'
    }
    if ($CreateResourceGroup) {
        Write-Host "  - Create resource group '$ResourceGroupName' when missing"
    }
    if ($SkipCodeDeployment) {
        Write-Host '  - Function App code deployment is skipped.'
    }
    else {
        Write-Host '  - Build and deploy the Function App code package'
    }

    Write-Host 'Existing shared resources referenced without creation:'
    Write-Host "  - Virtual network: $VirtualNetworkSubscriptionId/$VirtualNetworkResourceGroupName/$VirtualNetworkName"
    Write-Host "  - Integration subnet: $SubnetName"
    Write-Host "  - Private endpoint subnet: $PrivateEndpointSubnetName"
    Write-Host "  - Key Vault: $KeyVaultResourceGroupName/$KeyVaultName"
    Write-Host '  - The Active Directory service account is not changed during normal deployment.'
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & az @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed (az $($Arguments -join ' ')):`n$text"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Get-AzureJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = Invoke-AzureCli -Arguments ($Arguments + @('--only-show-errors', '--output', 'json'))
    if ([string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }
    $result.Output | ConvertFrom-Json -Depth 100
}

function ConvertTo-AzureCliJsonArgument {
    param([Parameter(Mandatory)][string]$Json)

    if ($IsWindows) {
        return $Json.Replace('"', '\"')
    }

    $Json
}

function Assert-Command {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$InstallHint
    )
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. $InstallHint"
    }
}

function Read-RequiredInstallerValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Prompt
    )

    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw "$Name must be supplied explicitly for noninteractive execution."
    }
    do {
        $value = Read-Host $Prompt
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }

        Write-Host "$Name cannot be empty. Please enter a value." -ForegroundColor Yellow
    } while ($true)
}

function Read-InstallerValueWithDefault {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [Parameter(Mandatory)][string]$DefaultValue
    )

    $enteredValue = Read-Host "$Prompt [$DefaultValue]"
    if ([string]::IsNullOrWhiteSpace($enteredValue)) {
        return $DefaultValue
    }

    $enteredValue.Trim()
}

function Read-InteractiveTaggingRule {
    $ruleName = Read-InstallerValueWithDefault `
        -Prompt 'Rule name' `
        -DefaultValue 'Rule1'
    $ruleDescription = Read-InstallerValueWithDefault `
        -Prompt 'Rule description' `
        -DefaultValue '1st Tagging Rule'
    $baseDn = Read-InstallerValueWithDefault `
        -Prompt 'Base DN' `
        -DefaultValue 'OU=Computers,OU=Admin'
    $extensionAttribute1Value = Read-InstallerValueWithDefault `
        -Prompt 'ExtensionAttribute1 value' `
        -DefaultValue 'Tier 0'

    @{
        Name = $ruleName
        Description = $ruleDescription
        BaseDn = $baseDn
        ExtensionAttributes = @{
            extensionAttribute1 = $extensionAttribute1Value
        }
    }
}

function Get-LoggedOnUserForestDomain {
    try {
        $forest = Get-ADForest -Current LoggedOnUser -ErrorAction Stop
        if (-not [string]::IsNullOrWhiteSpace([string]$forest.Name)) {
            return ([string]$forest.Name).Trim()
        }
    }
    catch {
        Write-Verbose "No Active Directory forest default was detected for the logged-on user: $($_.Exception.Message)"
    }

    $null
}

function ConvertTo-LdapFilterValue {
    param([Parameter(Mandatory)][string]$Value)

    $Value.Replace('\', '\5c').
        Replace('*', '\2a').
        Replace('(', '\28').
        Replace(')', '\29').
        Replace([string][char]0, '\00')
}

function Resolve-ADServiceAccountDistinguishedName {
    $escapedServiceAccount = ConvertTo-LdapFilterValue -Value $ADServiceAccount
    $parameters = @{
        LDAPFilter = "(userPrincipalName=$escapedServiceAccount)"
        Server = $ActiveDirectoryServer
        Properties = 'DistinguishedName'
        ErrorAction = 'Stop'
    }
    if ($null -ne $ActiveDirectoryProvisioningCredential) {
        $parameters.Credential = $ActiveDirectoryProvisioningCredential
    }
    elseif ($null -ne $ActiveDirectoryCredential) {
        $parameters.Credential = $ActiveDirectoryCredential
    }

    try {
        $accounts = @(Get-ADUser @parameters)
    }
    catch {
        Write-Verbose "The distinguished name for AD service account '$ADServiceAccount' could not be resolved automatically: $($_.Exception.Message)"
        return $null
    }

    if ($accounts.Count -gt 1) {
        throw "More than one Active Directory user has the UPN '$ADServiceAccount'."
    }
    if ($accounts.Count -eq 1) {
        return [string]$accounts[0].DistinguishedName
    }

    $null
}

function Convert-DnsNameToDistinguishedName {
    param([Parameter(Mandatory)][string]$DnsName)
    ($DnsName.Trim('.').Split('.') | ForEach-Object { "DC=$_" }) -join ','
}

function Test-ForestRelativeOuPath {
    param([Parameter(Mandatory)][string]$Path)
    $Path -notmatch '(?i)(?:^|(?<!\\),)\s*DC\s*='
}

function Test-ValidTaggingRuleName {
    param([Parameter(Mandatory)][string]$Name)
    $Name.Trim() -match '^[\p{L}\p{N}][\p{L}\p{N} ._()\-]{0,127}$'
}

function Test-ValidTaggingBaseDn {
    param([Parameter(Mandatory)][string]$Path)
    $valuePattern = '(?!#)(?:\\[^\x00-\x1F\x7F]|[^,+"\\<>;\x00-\x1F\x7F])+'
    $ouPattern = "(?i:OU)\s*=$valuePattern"
    $dcPattern = "(?i:DC)\s*=$valuePattern"
    $Path -match "^\s*(?:$ouPattern(?:\s*,\s*$ouPattern)*(?:\s*,\s*$dcPattern)*|$dcPattern(?:\s*,\s*$dcPattern)*)\s*$"
}

function Get-DomainDnsNameFromDistinguishedName {
    param([Parameter(Mandatory)][string]$DistinguishedName)
    @([regex]::Matches(
        $DistinguishedName,
        '(?i)(?:^|(?<!\\),)\s*DC\s*=\s*([^,]+)'
    ) | ForEach-Object { $_.Groups[1].Value.Trim() }) -join '.'
}

function Get-TaggingRuleValue {
    param(
        [Parameter(Mandatory)][object]$InputRule,
        [Parameter(Mandatory)][string]$PropertyName
    )

    if ($InputRule -is [System.Collections.IDictionary]) {
        return $InputRule[$PropertyName]
    }

    $property = $InputRule.PSObject.Properties[$PropertyName]
    if ($null -ne $property) {
        return $property.Value
    }
}

function ConvertTo-NormalizedTaggingRule {
    if ($TaggingRules.Count -eq 0) {
        throw 'At least one tagging rule is required.'
    }

    $normalizedRules = [System.Collections.Generic.List[object]]::new()
    $ruleNames = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($rule in $TaggingRules) {
        $name = [string](Get-TaggingRuleValue -InputRule $rule -PropertyName 'Name')
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Every tagging rule requires a non-empty Name.'
        }
        if (-not (Test-ValidTaggingRuleName $name)) {
            throw "Tagging rule name '$name' contains unsupported characters. Use 1-128 letters, numbers, spaces, dots, underscores, hyphens, or parentheses."
        }
        if (-not $ruleNames.Add($name.Trim())) {
            throw "Tagging rule name '$name' is duplicated."
        }
        $description = [string](Get-TaggingRuleValue -InputRule $rule -PropertyName 'Description')
        if ([string]::IsNullOrWhiteSpace($description)) {
            throw "Tagging rule '$name' requires a non-empty Description."
        }
        if ($description.Length -gt 1024) {
            throw "Tagging rule '$name' Description cannot exceed 1024 characters."
        }
        $baseDn = [string]$rule.BaseDn
        if ([string]::IsNullOrWhiteSpace($baseDn)) {
            throw 'Every tagging rule requires a non-empty BaseDn.'
        }
        if ($baseDn.Length -gt 2048) {
            throw "Tagging rule '$name' BaseDn cannot exceed 2048 characters."
        }
        if (-not (Test-ValidTaggingBaseDn $baseDn)) {
            throw "Tagging rule '$baseDn' must be a valid OU distinguished name with an optional trailing DC part."
        }

        $attributes = [System.Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase
        )
        $rawAttributes = Get-TaggingRuleValue -InputRule $rule -PropertyName 'ExtensionAttributes'

        $entries = if ($null -eq $rawAttributes) {
            @()
        }
        elseif ($rawAttributes -is [System.Collections.IDictionary]) {
            $rawAttributes.GetEnumerator() | ForEach-Object {
                [pscustomobject]@{ Name = [string]$_.Key; Value = [string]$_.Value }
            }
        }
        else {
            $rawAttributes.PSObject.Properties | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Value = [string]$_.Value }
            }
        }

        foreach ($entry in @($entries)) {
            if ($entry.Name -notmatch '^extensionAttribute(?:[1-9]|1[0-5])$') {
                throw "Tagging rule '$baseDn' contains unsupported attribute '$($entry.Name)'."
            }
            if ([string]::IsNullOrWhiteSpace($entry.Value)) {
                throw "Tagging rule '$baseDn' requires a non-empty value for '$($entry.Name)'."
            }
            if ($entry.Value.Length -gt 1024) {
                throw "Tagging rule '$name' value for '$($entry.Name)' cannot exceed 1024 characters."
            }
            if (-not $attributes.TryAdd($entry.Name, $entry.Value)) {
                throw "Tagging rule '$baseDn' contains duplicate attribute '$($entry.Name)'."
            }
        }
        if ($attributes.Count -gt 15) {
            throw "Tagging rule '$name' cannot contain more than 15 extension attributes."
        }

        $membershipTargets = [ordered]@{}
        foreach ($propertyName in @('EntraGroupIds', 'AdministrativeUnitIds')) {
            $targetIds = @(
                Get-TaggingRuleValue -InputRule $rule -PropertyName $propertyName |
                    Where-Object { $null -ne $_ }
            )
            if ($targetIds.Count -gt 50) {
                throw "Tagging rule '$name' cannot contain more than 50 $propertyName."
            }

            $normalizedIds = [System.Collections.Generic.List[guid]]::new()
            $uniqueIds = [System.Collections.Generic.HashSet[guid]]::new()
            foreach ($targetId in $targetIds) {
                $parsedId = [guid]::Empty
                if (-not [guid]::TryParse([string]$targetId, [ref]$parsedId) -or
                    $parsedId -eq [guid]::Empty) {
                    throw "Tagging rule '$name' contains an invalid or empty GUID in $propertyName."
                }
                if (-not $uniqueIds.Add($parsedId)) {
                    throw "Tagging rule '$name' contains duplicate ID '$parsedId' in $propertyName."
                }
                $normalizedIds.Add($parsedId)
            }
            $membershipTargets[$propertyName] = $normalizedIds.ToArray()
        }
        if ($attributes.Count -eq 0 -and
            $membershipTargets.EntraGroupIds.Count -eq 0 -and
            $membershipTargets.AdministrativeUnitIds.Count -eq 0) {
            throw "Tagging rule '$name' requires at least one extension attribute, Entra group, or administrative unit."
        }

        $normalizedRules.Add([ordered]@{
            name = $name.Trim()
            description = $description.Trim()
            baseDn = $baseDn
            extensionAttributes = $attributes
            entraGroupIds = $membershipTargets.EntraGroupIds
            administrativeUnitIds = $membershipTargets.AdministrativeUnitIds
        })
    }

    $normalizedRules
}

function Connect-LdapDirectory {
    param(
        [Parameter(Mandatory)][string]$Server,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][bool]$UseSsl,
        [Parameter()][pscredential]$Credential = $ActiveDirectoryCredential
    )

    $identifier = [System.DirectoryServices.Protocols.LdapDirectoryIdentifier]::new(
        $Server,
        $Port,
        $false,
        $false
    )
    $authenticationType = if ($UseSsl) {
        [System.DirectoryServices.Protocols.AuthType]::Basic
    }
    else {
        [System.DirectoryServices.Protocols.AuthType]::Negotiate
    }
    $networkCredential = $Credential.GetNetworkCredential()
    if (-not $UseSsl -and $networkCredential.UserName.Contains('@')) {
        $userPrincipalNameParts = $networkCredential.UserName.Split('@', 2)
        $networkCredential = [System.Net.NetworkCredential]::new(
            $userPrincipalNameParts[0],
            $networkCredential.Password,
            $userPrincipalNameParts[1])
    }
    $connection = [System.DirectoryServices.Protocols.LdapConnection]::new(
        $identifier,
        $networkCredential,
        $authenticationType
    )
    $connection.Timeout = [TimeSpan]::FromSeconds(15)
    $connection.SessionOptions.ProtocolVersion = 3
    $connection.SessionOptions.SecureSocketLayer = $UseSsl
    if ($UseSsl -and $script:TrustedCertificate) {
        $connection.SessionOptions.VerifyServerCertificate =
            [HybridDeviceTaggerLdapCertificateValidatorV2]::Create(
                $Server,
                $script:TrustedCertificate)
    }
    if (-not $UseSsl) {
        $connection.SessionOptions.Signing = $true
        $connection.SessionOptions.Sealing = $true
    }

    try {
        $connection.Bind()
        $connection
    }
    catch {
        $connection.Dispose()
        throw
    }
}

function Confirm-LdapFallbackUse {
    if ($script:EffectiveAllowLdapFallback) {
        return $true
    }

    $warning = @"
LDAPS is unavailable. Continuing with LDAP on port $LdapFallbackPort uses Negotiate authentication with LDAP signing and sealing.
The connection does not use TLS, but the negotiated SASL security layer protects the bind and LDAP traffic.
Password rotation will remain disabled until LDAPS becomes available.
"@
    Write-Warning $warning

    if ($AllowLdapFallback.IsPresent) {
        Write-Warning 'LDAP fallback was explicitly approved through -AllowLdapFallback.'
        return $true
    }

    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'Interactive confirmation is unavailable in CI. Re-run with -AllowLdapFallback to explicitly approve unencrypted LDAP.'
    }

    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new('&Yes', 'Use unencrypted LDAP for read and bind operations.'),
        [System.Management.Automation.Host.ChoiceDescription]::new('&No', 'Cancel without enabling LDAP fallback.')
    )
    try {
        $choice = $Host.UI.PromptForChoice(
            'Approve unencrypted LDAP fallback?',
            'Do you want to continue with LDAP instead of LDAPS?',
            $choices,
            1
        )
    }
    catch {
        throw 'LDAP fallback requires interactive confirmation. Re-run with -AllowLdapFallback for noninteractive execution.'
    }

    if ($choice -ne 0) {
        throw 'Installation canceled because LDAPS is unavailable and LDAP fallback was not approved.'
    }

    $true
}

function Connect-ValidatedLdapDirectory {
    param([Parameter(Mandatory)][string]$Server)

    try {
        Connect-LdapDirectory -Server $Server -Port 636 -UseSsl $true
    }
    catch {
        if (-not (Test-LdapConnectionUnavailable -Exception $_.Exception)) {
            throw
        }

        if ($script:TrustedCertificate -and
            [HybridDeviceTaggerLdapCertificateValidatorV2]::ValidateTlsEndpoint(
                $Server,
                636,
                $script:TrustedCertificate)) {
            Write-Warning @"
The native Windows LDAP client could not bind over LDAPS, but the strict TLS certificate handshake succeeded.
Installer directory checks will use LDAP with Negotiate signing and sealing. The deployed Function remains LDAPS-only.
"@
            return Connect-LdapDirectory `
                -Server $Server `
                -Port $LdapFallbackPort `
                -UseSsl $false
        }

        $null = Confirm-LdapFallbackUse
        $connection = Connect-LdapDirectory `
            -Server $Server `
            -Port $LdapFallbackPort `
            -UseSsl $false
        $script:EffectiveAllowLdapFallback = $true
        $connection
    }
}

function Get-LdapServerForDomain {
    param([Parameter(Mandatory)][string]$DomainDnsName)

    if ($ActiveDirectoryServer.EndsWith(
        ".$DomainDnsName",
        [StringComparison]::OrdinalIgnoreCase
    )) {
        return $ActiveDirectoryServer
    }

    $DomainDnsName
}

function Get-ActiveDirectoryAccountName {
    $accountName = if ($ADServiceAccount.Contains('@')) {
        $ADServiceAccount.Split('@', 2)[0]
    }
    elseif ($ADServiceAccount.Contains('\')) {
        $ADServiceAccount.Split('\', 2)[1]
    }
    else {
        $ADServiceAccount
    }
    if ($accountName.Length -gt 20) {
        throw "The sAMAccountName derived from '$ADServiceAccount' exceeds 20 characters."
    }
    $accountName
}

function New-ComplexRandomPassword {
    param([Parameter(Mandatory)][ValidateRange(4, 256)][int]$Length)

    $characterSets = @(
        'ABCDEFGHJKLMNPQRSTUVWXYZ',
        'abcdefghijkmnopqrstuvwxyz',
        '23456789',
        '!#$%&*+-=?@_'
    )
    $allCharacters = $characterSets -join ''
    $characters = [char[]]::new($Length)

    for ($index = 0; $index -lt $characterSets.Count; $index++) {
        $set = $characterSets[$index]
        $characters[$index] = $set[[Security.Cryptography.RandomNumberGenerator]::GetInt32($set.Length)]
    }
    for ($index = $characterSets.Count; $index -lt $characters.Length; $index++) {
        $characters[$index] = $allCharacters[
            [Security.Cryptography.RandomNumberGenerator]::GetInt32($allCharacters.Length)
        ]
    }
    for ($index = $characters.Length - 1; $index -gt 0; $index--) {
        $swapIndex = [Security.Cryptography.RandomNumberGenerator]::GetInt32($index + 1)
        $temporary = $characters[$index]
        $characters[$index] = $characters[$swapIndex]
        $characters[$swapIndex] = $temporary
    }

    -join $characters
}

function Set-RandomActiveDirectoryCredential {
    $plainPassword = New-ComplexRandomPassword -Length $InitialPasswordLength
    try {
        $securePassword = [Security.SecureString]::new()
        foreach ($character in $plainPassword.ToCharArray()) {
            $securePassword.AppendChar($character)
        }
        $securePassword.MakeReadOnly()
        $script:ActiveDirectoryCredential = [pscredential]::new(
            $ADServiceAccount,
            $securePassword
        )
    }
    finally {
        $plainPassword = $null
    }
}

function Select-ActiveDirectoryResetCredential {
    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Randomized',
            'Reset to a cryptographically random complex password.'
        ),
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Enter',
            'Enter the new password securely.'
        ),
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Cancel',
            'Cancel without changing Active Directory.'
        )
    )
    $choice = $Host.UI.PromptForChoice(
        'Reset Active Directory service-account password?',
        "Choose the new password source for '$ActiveDirectoryBindUserDistinguishedName'.",
        $choices,
        2
    )

    switch ($choice) {
        0 { Set-RandomActiveDirectoryCredential }
        1 {
            $script:ActiveDirectoryCredential = Get-Credential `
                -UserName $ADServiceAccount `
                -Message 'Enter the new Active Directory service-account password'
            if ($null -eq $script:ActiveDirectoryCredential) {
                throw 'Password entry was canceled without changing Active Directory.'
            }
        }
        default { throw 'Installation canceled without changing the Active Directory service account.' }
    }
}

function Set-ActiveDirectoryAccountPassword {
    param(
        [Parameter(Mandatory)][System.DirectoryServices.Protocols.LdapConnection]$Connection,
        [Parameter(Mandatory)][bool]$EnableAccount
    )

    $quotedPassword = '"' + $ActiveDirectoryCredential.GetNetworkCredential().Password + '"'
    $passwordBytes = [Text.Encoding]::Unicode.GetBytes($quotedPassword)
    try {
        $passwordChange = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
        $passwordChange.Name = 'unicodePwd'
        $passwordChange.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
        $null = $passwordChange.Add($passwordBytes)
        $passwordRequest = [System.DirectoryServices.Protocols.ModifyRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            $passwordChange
        )
        $null = $Connection.SendRequest($passwordRequest)

        $attributesRequest = [System.DirectoryServices.Protocols.ModifyRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName
        )
        $pwdLastSet = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
        $pwdLastSet.Name = 'pwdLastSet'
        $pwdLastSet.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
        $null = $pwdLastSet.Add('-1')
        $null = $attributesRequest.Modifications.Add($pwdLastSet)
        if ($EnableAccount) {
            $userAccountControl = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
            $userAccountControl.Name = 'userAccountControl'
            $userAccountControl.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
            $null = $userAccountControl.Add('512')
            $null = $attributesRequest.Modifications.Add($userAccountControl)
        }
        $null = $Connection.SendRequest($attributesRequest)
    }
    finally {
        [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
    }
}

function Test-LdapConnectionUnavailable {
    param([Parameter(Mandatory)][Exception]$Exception)

    $currentException = $Exception
    while ($null -ne $currentException) {
        if ($currentException -is [System.DirectoryServices.Protocols.LdapException]) {
            return $currentException.ErrorCode -in 51, 52, 81, 85, 91
        }
        $currentException = $currentException.InnerException
    }

    $false
}

function Initialize-ActiveDirectoryServiceAccountWithAdModule {
    Import-Module ActiveDirectory -ErrorAction Stop

    try {
        $account = Get-ADUser `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -ErrorAction Stop
    }
    catch {
        if ($_.Exception.GetType().FullName -ne 'Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException') {
            throw
        }
        $account = $null
    }

    if ($null -ne $account) {
        Select-ActiveDirectoryResetCredential
        Set-ADAccountPassword `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -Reset `
            -NewPassword $script:ActiveDirectoryCredential.Password
        Set-ADUser `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -ChangePasswordAtLogon $false
        $script:ActiveDirectoryPasswordChanged = $true
        Write-Host 'The AD service-account password was reset through Active Directory Web Services; password change at next logon is disabled.' -ForegroundColor Green
        return
    }

    Set-RandomActiveDirectoryCredential
    $dnMatch = [regex]::Match($ActiveDirectoryBindUserDistinguishedName, '(?i)^CN=([^,]+),(.+)$')
    if (-not $dnMatch.Success) {
        throw 'ActiveDirectoryBindUserDistinguishedName must start with an unescaped CN followed by its parent DN.'
    }

    $parameters = @{
        Name = $dnMatch.Groups[1].Value
        Description = $activeDirectoryServiceAccountDescription
        SamAccountName = Get-ActiveDirectoryAccountName
        Path = $dnMatch.Groups[2].Value
        Server = $ActiveDirectoryServer
        Credential = $script:ActiveDirectoryProvisioningCredential
        AccountPassword = $script:ActiveDirectoryCredential.Password
        Enabled = $true
        ChangePasswordAtLogon = $false
    }
    if ($ADServiceAccount.Contains('@')) {
        $parameters.UserPrincipalName = $ADServiceAccount
    }
    New-ADUser @parameters
    $script:ActiveDirectoryPasswordChanged = $true
    Write-Host 'The AD service account was created through Active Directory Web Services with a randomized complex password and enabled; password change at next logon is disabled.' -ForegroundColor Green
}

function Initialize-ActiveDirectoryServiceAccount {
    Add-Type -AssemblyName System.DirectoryServices.Protocols

    if ($ValidateOnly -or $WhatIfPreference) {
        return
    }
    if ($null -eq $ActiveDirectoryProvisioningCredential) {
        $script:ActiveDirectoryProvisioningCredential = Get-Credential `
            -Message 'Credential permitted to create or reset the configured AD service account'
    }
    if ($null -eq $script:ActiveDirectoryProvisioningCredential) {
        throw 'An Active Directory provisioning credential is required for installation.'
    }

    $connection = $null
    try {
        try {
            $connection = Connect-LdapDirectory `
                -Server $ActiveDirectoryServer `
                -Port 636 `
                -UseSsl $true `
                -Credential $script:ActiveDirectoryProvisioningCredential
        }
        catch {
            if (-not (Test-LdapConnectionUnavailable -Exception $_.Exception)) {
                throw
            }
            $null = Confirm-LdapFallbackUse
            Write-Warning 'Using the Active Directory PowerShell module over Active Directory Web Services for account bootstrap because LDAPS is unavailable.'
            Initialize-ActiveDirectoryServiceAccountWithAdModule
            return
        }
        $searchRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            '(objectClass=user)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('distinguishedName')
        )
        try {
            $response = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($searchRequest)
            $accountExists = $response.Entries.Count -eq 1
        }
        catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
            if ($_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::NoSuchObject) {
                throw
            }
            $accountExists = $false
        }

        if ($accountExists) {
            Select-ActiveDirectoryResetCredential
            Set-ActiveDirectoryAccountPassword -Connection $connection -EnableAccount $false
            $script:ActiveDirectoryPasswordChanged = $true
            Write-Host 'The AD service-account password was reset securely; password change at next logon is disabled.' -ForegroundColor Green
            return
        }

        Set-RandomActiveDirectoryCredential
        $dnMatch = [regex]::Match($ActiveDirectoryBindUserDistinguishedName, '(?i)^CN=([^,]+),(.+)$')
        if (-not $dnMatch.Success) {
            throw 'ActiveDirectoryBindUserDistinguishedName must start with an unescaped CN followed by its parent DN.'
        }
        $addRequest = [System.DirectoryServices.Protocols.AddRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName
        )
        $objectClass = [System.DirectoryServices.Protocols.DirectoryAttribute]::new('objectClass')
        foreach ($className in @('top', 'person', 'organizationalPerson', 'user')) {
            $null = $objectClass.Add($className)
        }
        $null = $addRequest.Attributes.Add($objectClass)
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new('cn', $dnMatch.Groups[1].Value)
        )
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new(
                'description',
                $activeDirectoryServiceAccountDescription
            )
        )
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new(
                'sAMAccountName',
                (Get-ActiveDirectoryAccountName)
            )
        )
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new('userAccountControl', '514')
        )
        if ($ADServiceAccount.Contains('@')) {
            $null = $addRequest.Attributes.Add(
                [System.DirectoryServices.Protocols.DirectoryAttribute]::new(
                    'userPrincipalName',
                    $ADServiceAccount
                )
            )
        }
        $null = $connection.SendRequest($addRequest)
        Set-ActiveDirectoryAccountPassword -Connection $connection -EnableAccount $true
        $script:ActiveDirectoryPasswordChanged = $true
        Write-Host 'The AD service account was created with a randomized complex password and enabled; password change at next logon is disabled.' -ForegroundColor Green
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }
}

function Test-LdapConfiguration {
    Add-Type -AssemblyName System.DirectoryServices.Protocols

    if ($ActiveDirectoryCredential.UserName -ine $ADServiceAccount) {
        throw "ActiveDirectoryCredential.UserName must match ADServiceAccount."
    }

    $connection = $null
    $script:EffectiveAllowLdapFallback = $false
    try {
        $connection = Connect-ValidatedLdapDirectory -Server $ActiveDirectoryServer
        $rootRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            '',
            '(objectClass=*)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('configurationNamingContext', 'defaultNamingContext', 'rootDomainNamingContext', 'dnsHostName')
        )
        $rootResponse = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($rootRequest)
        if ($rootResponse.Entries.Count -ne 1) {
            throw 'The LDAP RootDSE query did not return exactly one entry.'
        }

        $expectedForestDn = Convert-DnsNameToDistinguishedName $ActiveDirectoryForestDomain
        $rootDomainNamingContext = [string]$rootResponse.Entries[0].Attributes['rootDomainNamingContext'][0]
        if ($rootDomainNamingContext -ine $expectedForestDn) {
            throw "AD forest mismatch. Expected '$expectedForestDn', received '$rootDomainNamingContext'."
        }

        $configurationNamingContext = [string]$rootResponse.Entries[0].Attributes['configurationNamingContext'][0]
        $partitionsRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            "CN=Partitions,$configurationNamingContext",
            '(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3))',
            [System.DirectoryServices.Protocols.SearchScope]::OneLevel,
            [string[]]@('nCName', 'dnsRoot')
        )
        $partitionsResponse = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($partitionsRequest)
        $forestDomains = @($partitionsResponse.Entries | ForEach-Object {
            [pscustomobject]@{
                NamingContext = [string]$_.Attributes['nCName'][0]
                DnsRoot = [string]$_.Attributes['dnsRoot'][0]
            }
        } | Where-Object { $_.NamingContext -and $_.DnsRoot })
        if ($forestDomains.Count -eq 0) {
            throw 'No Active Directory domains were found in the forest configuration partition.'
        }
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }

    foreach ($configuredBaseDn in @($script:NormalizedTaggingRules.baseDn | Select-Object -Unique)) {
        $isForestRelative = Test-ForestRelativeOuPath $configuredBaseDn
        $searchTargets = if ($isForestRelative) {
            @($forestDomains | ForEach-Object {
                [pscustomobject]@{
                    Server = Get-LdapServerForDomain $_.DnsRoot
                    BaseDn = "$($configuredBaseDn.Trim().TrimEnd(',')),$($_.NamingContext)"
                }
            })
        }
        else {
            @([pscustomobject]@{
                Server = Get-LdapServerForDomain (
                    Get-DomainDnsNameFromDistinguishedName $configuredBaseDn
                )
                BaseDn = $configuredBaseDn
            })
        }
        $resolvedOuCount = 0

        foreach ($target in $searchTargets) {
            $domainConnection = $null
            try {
                $domainConnection = Connect-ValidatedLdapDirectory -Server $target.Server
                $baseRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
                    $target.BaseDn,
                    '(objectClass=*)',
                    [System.DirectoryServices.Protocols.SearchScope]::Base,
                    [string[]]@('distinguishedName')
                )
                $baseResponse = [System.DirectoryServices.Protocols.SearchResponse]$domainConnection.SendRequest($baseRequest)
                if ($baseResponse.Entries.Count -ne 1) {
                    throw "Active Directory base DN '$($target.BaseDn)' was not found."
                }
                $resolvedOuCount++

                $deviceRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
                    $target.BaseDn,
                    '(&(objectCategory=computer)(userCertificate=*))',
                    [System.DirectoryServices.Protocols.SearchScope]::Subtree,
                    [string[]]@('objectGUID')
                )
                $deviceRequest.SizeLimit = 1
                try {
                    $null = $domainConnection.SendRequest($deviceRequest)
                }
                catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
                    if ($_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::SizeLimitExceeded) {
                        throw
                    }
                }
            }
            catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
                if (-not $isForestRelative -or
                    $_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::NoSuchObject) {
                    throw
                }
                Write-Host "OU path '$configuredBaseDn' is absent in domain '$($target.Server)'; skipping that domain."
            }
            finally {
                if ($null -ne $domainConnection) {
                    $domainConnection.Dispose()
                }
            }
        }

        if ($isForestRelative -and $resolvedOuCount -eq 0) {
            throw "OU path '$configuredBaseDn' was not found in any domain in the forest."
        }
    }

    $accountServer = Get-DomainDnsNameFromDistinguishedName $ActiveDirectoryBindUserDistinguishedName
    $accountConnection = $null
    try {
        $accountConnection = Connect-ValidatedLdapDirectory -Server $accountServer
        $accountRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            '(objectClass=user)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('distinguishedName', 'userAccountControl', 'pwdLastSet')
        )
        $accountResponse = [System.DirectoryServices.Protocols.SearchResponse]$accountConnection.SendRequest($accountRequest)
        if ($accountResponse.Entries.Count -ne 1) {
            throw "AD service account '$ActiveDirectoryBindUserDistinguishedName' was not found."
        }
    }
    finally {
        if ($null -ne $accountConnection) {
            $accountConnection.Dispose()
        }
    }

    if (-not $script:EffectiveAllowLdapFallback) {
        Write-Host 'LDAPS validation succeeded for all required domains. LDAP fallback will be disabled.' -ForegroundColor Green
    }
}

function Assert-Subnet {
    param([Parameter(Mandatory)]$Subnet)

    if ($SubnetName.Contains('_')) {
        throw "Subnet name '$SubnetName' contains an underscore, which FC1 does not support."
    }

    $prefixes = @()
    if ($null -ne $Subnet.PSObject.Properties['addressPrefix'] -and $Subnet.addressPrefix) {
        $prefixes += $Subnet.addressPrefix
    }
    if ($null -ne $Subnet.PSObject.Properties['addressPrefixes'] -and $Subnet.addressPrefixes) {
        $prefixes += @($Subnet.addressPrefixes)
    }
    $ipv4Prefixes = @($prefixes | Where-Object { $_ -match '/(?<length>\d+)$' -and $_ -notmatch ':' })
    if ($ipv4Prefixes.Count -eq 0) {
        throw 'The integration subnet must have an IPv4 address prefix.'
    }
    foreach ($prefix in $ipv4Prefixes) {
        $prefixLength = [int]($prefix -replace '^.*/', '')
        if ($prefixLength -gt 27) {
            throw "Subnet prefix '$prefix' is smaller than the FC1 minimum recommendation of /27."
        }
    }

    $delegations = @($Subnet.delegations | ForEach-Object { $_.serviceName })
    if ($delegations -notcontains 'Microsoft.App/environments') {
        throw "FC1 integration subnet '$SubnetName' is not delegated to 'Microsoft.App/environments'. Select a subnet whose Subnet delegation is set exclusively to 'Microsoft.App/environments'."
    }
    if (@($delegations | Where-Object { $_ -ne 'Microsoft.App/environments' }).Count -gt 0) {
        throw "Subnet '$SubnetName' has unsupported additional delegations: $($delegations -join ', ')."
    }
    if (@($Subnet.serviceEndpoints).Count -gt 0) {
        throw "FC1 integration subnet '$SubnetName' must not contain service endpoints."
    }
}

function Assert-PrivateEndpointSubnet {
    param([Parameter(Mandatory)]$Subnet)

    if ($PrivateEndpointSubnetName -ieq $SubnetName) {
        throw 'The private endpoint subnet must differ from the FC1 integration subnet.'
    }
    $prefixes = @()
    if ($null -ne $Subnet.PSObject.Properties['addressPrefix'] -and $Subnet.addressPrefix) {
        $prefixes += $Subnet.addressPrefix
    }
    if ($null -ne $Subnet.PSObject.Properties['addressPrefixes'] -and $Subnet.addressPrefixes) {
        $prefixes += @($Subnet.addressPrefixes)
    }
    if (@($prefixes | Where-Object { $_ -match '/\d+$' -and $_ -notmatch ':' }).Count -eq 0) {
        throw "Private endpoint subnet '$PrivateEndpointSubnetName' must have an IPv4 address prefix."
    }
    if (@($Subnet.delegations).Count -gt 0) {
        throw "Private endpoint subnet '$PrivateEndpointSubnetName' must not be delegated."
    }
}

function Assert-KeyVault {
    param(
        [Parameter(Mandatory)]$Vault,
        [switch]$SkipCurrentCredential
    )

    if (-not $Vault.enableRbacAuthorization) {
        throw "Key Vault '$KeyVaultName' must use Azure RBAC authorization."
    }
    if (-not $Vault.enableSoftDelete) {
        throw "Key Vault '$KeyVaultName' must have soft delete enabled."
    }
    if (-not $Vault.enablePurgeProtection) {
        throw "Key Vault '$KeyVaultName' must have purge protection enabled."
    }
    if ($Vault.publicNetworkAccess -eq 'Disabled' -or $Vault.networkAcls.defaultAction -eq 'Deny') {
        $approvedConnections = @($Vault.privateEndpointConnections | Where-Object {
            $_.privateLinkServiceConnectionState.status -eq 'Approved'
        })
        if ($approvedConnections.Count -eq 0) {
            throw "Restricted Key Vault '$KeyVaultName' requires an approved private endpoint connection."
        }
    }

    if ($SkipCurrentCredential) {
        return
    }

    $secretResourceId = "/subscriptions/$KeyVaultSubscriptionId/resourceGroups/" +
        "$KeyVaultResourceGroupName/providers/Microsoft.KeyVault/vaults/" +
        "$KeyVaultName/secrets/$CurrentCredentialEntryName"
    $secret = Invoke-AzureCli -Arguments @(
        'resource', 'show',
        '--ids', $secretResourceId,
        '--subscription', $KeyVaultSubscriptionId,
        '--api-version', '2024-11-01',
        '--query', '{enabled:properties.attributes.enabled,expires:properties.attributes.exp,id:id}',
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($secret.ExitCode -ne 0) {
        throw "Current AD password secret '$CurrentCredentialEntryName' is missing or inaccessible in Key Vault '$KeyVaultName'."
    }

    $secretMetadata = $secret.Output | ConvertFrom-Json
    if ($secretMetadata.enabled -eq $false) {
        throw "Current AD password secret '$CurrentCredentialEntryName' is disabled."
    }
    if ($secretMetadata.expires) {
        $minimumExpiry = [DateTimeOffset]::UtcNow.AddDays(25)
        $secretExpiry = [DateTimeOffset]::FromUnixTimeSeconds($secretMetadata.expires)
        if ($secretExpiry -le $minimumExpiry) {
            throw "Current AD password secret expires before the 25-day rotation interval."
        }
    }

}

function Set-CurrentPasswordSecret {
    param([Parameter(Mandatory)][string]$VaultUri)

    $tokenResult = Invoke-AzureCli -Arguments @(
        'account', 'get-access-token', '--subscription', $KeyVaultSubscriptionId,
        '--resource', 'https://vault.azure.net', '--only-show-errors', '--output', 'json'
    )
    $accessToken = ($tokenResult.Output | ConvertFrom-Json).accessToken
    $plainPassword = $ActiveDirectoryCredential.GetNetworkCredential().Password
    try {
        $body = @{
            value = $plainPassword
            attributes = @{
                enabled = $true
                exp = [DateTimeOffset]::UtcNow.AddDays($InitialSecretExpiryDays).ToUnixTimeSeconds()
            }
        } | ConvertTo-Json -Compress
        $secretUri = '{0}/secrets/{1}?api-version=7.4' -f $VaultUri.TrimEnd('/'), $CurrentCredentialEntryName
        $null = Invoke-RestMethod `
            -Method Put `
            -Uri $secretUri `
            -Headers @{ Authorization = "Bearer $accessToken" } `
            -ContentType 'application/json' `
            -Body $body
    }
    finally {
        $plainPassword = $null
        $body = $null
        $accessToken = $null
    }

    Write-Host "The current AD password was stored in Key Vault '$KeyVaultName'." -ForegroundColor Green
}

function Assert-ExistingOwnedResource {
    $storage = Invoke-AzureCli -Arguments @(
        'storage', 'account', 'show', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $StorageAccountName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($storage.ExitCode -eq 0) {
        $storageAccount = $storage.Output | ConvertFrom-Json
        if ($storageAccount.location -ine $Location -or $storageAccount.kind -ne 'StorageV2') {
            throw "Existing storage account '$StorageAccountName' is not a StorageV2 account in '$Location'."
        }
        if ($storageAccount.minimumTlsVersion -ne 'TLS1_2' -or $storageAccount.allowBlobPublicAccess -ne $false) {
            throw "Existing storage account '$StorageAccountName' must enforce TLS 1.2 and disable public blob access."
        }
    }
    else {
        $availability = Get-AzureJson -Arguments @(
            'storage', 'account', 'check-name', '--subscription', $SubscriptionId,
            '--name', $StorageAccountName
        )
        if (-not $availability.nameAvailable) {
            throw "Storage account name '$StorageAccountName' is unavailable: $($availability.message)"
        }
    }

    $app = Invoke-AzureCli -Arguments @(
        'functionapp', 'show', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $FunctionAppName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($app.ExitCode -eq 0) {
        $functionApp = $app.Output | ConvertFrom-Json
        $functionAppLocation = ([string]$functionApp.location) -replace '[^a-zA-Z0-9]', ''
        if ($functionAppLocation -ine $Location -or $functionApp.kind -notmatch 'functionapp' -or $functionApp.kind -notmatch 'linux') {
            throw "Existing app '$FunctionAppName' is not a Linux Function App in '$Location'."
        }
        if ($functionApp.identity.type -notmatch 'SystemAssigned') {
            throw "Existing Function App '$FunctionAppName' must have a system-assigned identity."
        }
        $functionAppSubnetId = if ($null -ne $functionApp.PSObject.Properties['virtualNetworkSubnetId']) {
            [string]$functionApp.virtualNetworkSubnetId
        }
        else {
            ''
        }
        if ($functionAppSubnetId -and $functionAppSubnetId -ine $script:ExpectedSubnetId) {
            throw "Existing Function App '$FunctionAppName' is integrated with a different subnet."
        }
    }
    else {
        $availability = Get-AzureJson -Arguments @(
            'rest', '--method', 'post',
            '--url', "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Web/checkNameAvailability?api-version=2024-04-01",
            '--body', (ConvertTo-AzureCliJsonArgument -Json (
                @{ name = $FunctionAppName; type = 'Microsoft.Web/sites' } | ConvertTo-Json -Compress
            )),
            '--headers', 'Content-Type=application/json'
        )
        if (-not $availability.nameAvailable) {
            throw "Function App name '$FunctionAppName' is unavailable: $($availability.message)"
        }
    }
}

function Get-DeploymentParameter {
    $values = [ordered]@{
        location = $Location
        functionAppName = $FunctionAppName
        storageAccountName = $StorageAccountName
        virtualNetworkName = $VirtualNetworkName
        subnetName = $SubnetName
        privateEndpointSubnetName = $PrivateEndpointSubnetName
        virtualNetworkResourceGroupName = $VirtualNetworkResourceGroupName
        virtualNetworkSubscriptionId = $VirtualNetworkSubscriptionId
        keyVaultName = $KeyVaultName
        keyVaultResourceGroupName = $KeyVaultResourceGroupName
        keyVaultSubscriptionId = $KeyVaultSubscriptionId
        activeDirectoryServer = $ActiveDirectoryServer
        allowLdapFallback = $script:EffectiveAllowLdapFallback
        ldapFallbackPort = $LdapFallbackPort
        activeDirectoryTrustedCertificatePath = if ($ActiveDirectoryTrustedCertificatePath) {
            $trustedCertificatePackagePath
        } else {
            ''
        }
        activeDirectoryBindUsername = $ADServiceAccount
        activeDirectoryBindUserDistinguishedName = $ActiveDirectoryBindUserDistinguishedName
        managementApiClientId = $ManagementApiClientId
        managementApiAllowedPrincipalObjectIds = @($ManagementApiAllowedPrincipalObjectId | ForEach-Object ToString)
        taggingRules = $script:NormalizedTaggingRules
        currentPasswordSecretName = $CurrentCredentialEntryName
        nextPasswordSecretName = $StagedCredentialEntryName
        taggingSchedule = $TaggingSchedule
        rotationSchedule = $RotationSchedule
        maximumInstanceCount = $MaximumInstanceCount
        instanceMemoryMB = $InstanceMemoryMB
        tags = $Tags
    }

    $parameters = [ordered]@{}
    foreach ($entry in $values.GetEnumerator()) {
        $parameters[$entry.Key] = @{ value = $entry.Value }
    }
    @{ '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'; contentVersion = '1.0.0.0'; parameters = $parameters }
}

function Grant-GraphApplicationPermission {
    param([Parameter(Mandatory)][string]$PrincipalId)

    $graphServicePrincipal = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', 'https://graph.microsoft.com/v1.0/servicePrincipals?$filter=appId%20eq%20%2700000003-0000-0000-c000-000000000000%27'
    )
    $graph = @($graphServicePrincipal.value)[0]
    if (-not $graph) {
        throw 'Microsoft Graph service principal was not found in the tenant.'
    }
    $assignments = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$PrincipalId/appRoleAssignments"
    )
    foreach ($permission in @(
        'Device.ReadWrite.All',
        'GroupMember.ReadWrite.All',
        'AdministrativeUnit.ReadWrite.All'
    )) {
        $appRole = @($graph.appRoles | Where-Object {
            $_.value -eq $permission -and $_.allowedMemberTypes -contains 'Application'
        })[0]
        if (-not $appRole) {
            throw "Microsoft Graph application role $permission was not found."
        }

        $existing = @($assignments.value | Where-Object {
            $_.resourceId -eq $graph.id -and $_.appRoleId -eq $appRole.id
        })
        if ($existing.Count -gt 0) {
            Write-Host "Microsoft Graph $permission is already assigned."
            continue
        }

        $body = ConvertTo-AzureCliJsonArgument -Json (@{
            principalId = $PrincipalId
            resourceId = $graph.id
            appRoleId = $appRole.id
        } | ConvertTo-Json -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'post',
            '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$PrincipalId/appRoleAssignments",
            '--headers', 'Content-Type=application/json',
            '--body', $body,
            '--only-show-errors'
        )
    }
}

function Get-RestrictedAdministrativeUnitTarget {
    $administrativeUnitIds = @(
        $script:NormalizedTaggingRules |
            ForEach-Object {
                @(Get-TaggingRuleValue -InputRule $_ -PropertyName 'administrativeUnitIds')
            } |
            Select-Object -Unique
    )
    $restrictedTargets = [System.Collections.Generic.List[object]]::new()
    foreach ($administrativeUnitId in $administrativeUnitIds) {
        $administrativeUnit = Get-AzureJson -Arguments @(
            'rest', '--method', 'get',
            '--url', "https://graph.microsoft.com/v1.0/directory/administrativeUnits/$administrativeUnitId`?`$select=id,displayName,isMemberManagementRestricted"
        )
        if ($administrativeUnit.isMemberManagementRestricted -eq $true) {
            $restrictedTargets.Add($administrativeUnit)
        }
    }
    $restrictedTargets.ToArray()
}

function Grant-RestrictedAdministrativeUnitRole {
    param(
        [Parameter(Mandatory)][string]$PrincipalId,
        [Parameter(Mandatory)][object[]]$AdministrativeUnits
    )

    if ($AdministrativeUnits.Count -eq 0) {
        return
    }

    $roleName = 'HybridDeviceTagger Restricted Device Attribute Writer'
    $encodedRoleName = [uri]::EscapeDataString("displayName eq '$roleName'")
    $roleDefinitions = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/roleManagement/directory/roleDefinitions?`$filter=$encodedRoleName"
    )
    $roleDefinition = @($roleDefinitions.value)[0]
    if (-not $roleDefinition) {
        $roleBody = ConvertTo-AzureCliJsonArgument -Json (@{
            displayName = $roleName
            description = 'Updates only device extension attributes for HybridDeviceTagger-managed RMAU devices.'
            isEnabled = $true
            rolePermissions = @(@{
                allowedResourceActions = @(
                    'microsoft.directory/devices/allProperties/read',
                    'microsoft.directory/devices/extensionAttributeSet1/update',
                    'microsoft.directory/devices/extensionAttributeSet2/update',
                    'microsoft.directory/devices/extensionAttributeSet3/update'
                )
            })
        } | ConvertTo-Json -Depth 10 -Compress)
        $roleDefinition = Get-AzureJson -Arguments @(
            'rest', '--method', 'post',
            '--url', 'https://graph.microsoft.com/v1.0/roleManagement/directory/roleDefinitions',
            '--headers', 'Content-Type=application/json',
            '--body', $roleBody
        )
    }

    $encodedPrincipalFilter = [uri]::EscapeDataString("principalId eq '$PrincipalId'")
    $roleAssignments = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?`$filter=$encodedPrincipalFilter"
    )
    foreach ($administrativeUnit in $AdministrativeUnits) {
        $directoryScopeId = "/administrativeUnits/$($administrativeUnit.id)"
        $existing = @($roleAssignments.value | Where-Object {
            $_.roleDefinitionId -eq $roleDefinition.id -and
            $_.directoryScopeId -eq $directoryScopeId
        })
        if ($existing.Count -gt 0) {
            Write-Host "RMAU role is already assigned for '$($administrativeUnit.displayName)' ($($administrativeUnit.id))."
            continue
        }

        $assignmentBody = ConvertTo-AzureCliJsonArgument -Json (@{
            principalId = $PrincipalId
            roleDefinitionId = $roleDefinition.id
            directoryScopeId = $directoryScopeId
        } | ConvertTo-Json -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'post',
            '--url', 'https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments',
            '--headers', 'Content-Type=application/json',
            '--body', $assignmentBody,
            '--only-show-errors'
        )
    }
}

function Publish-FunctionApp {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$([guid]::NewGuid())"
    $publishPath = Join-Path $temporaryRoot 'publish'
    $zipPath = Join-Path $temporaryRoot 'HybridDeviceTagger.zip'
    try {
        $null = New-Item -ItemType Directory -Path $publishPath -Force
        & dotnet publish $projectPath --configuration Release --output $publishPath
        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet publish failed.'
        }
        if ($ActiveDirectoryTrustedCertificatePath) {
            $certificateDirectory = Join-Path $publishPath 'certificates'
            $null = New-Item -ItemType Directory -Path $certificateDirectory -Force
            [System.IO.File]::WriteAllText(
                (Join-Path $publishPath $trustedCertificatePackagePath),
                $script:TrustedCertificate.ExportCertificatePem(),
                [System.Text.Encoding]::ASCII)
        }
        Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $null = Invoke-AzureCli -Arguments @(
            'functionapp', 'deployment', 'source', 'config-zip',
            '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName,
            '--name', $FunctionAppName,
            '--src', $zipPath,
            '--only-show-errors'
        )
    }
    finally {
        if (Test-Path $temporaryRoot) {
            Remove-Item -Path $temporaryRoot -Recurse -Force
        }
    }
}

$installerLogPath = Join-Path ([System.IO.Path]::GetTempPath()) 'HybridDeviceTagger-Install.log'
$transcriptStarted = $false
$compactFailure = $false
try {
    Start-Transcript -Path $installerLogPath -Force | Out-Null
    $transcriptStarted = $true
    Write-Host "Installer log: $installerLogPath"

Write-Step 'Validating Azure subscription'
Assert-Command -Name az -InstallHint 'Install Azure CLI 2.60.0 or newer.'
$account = Get-AzureJson -Arguments @('account', 'show')
if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'SubscriptionId must be supplied explicitly for noninteractive execution.'
    }
    $enteredSubscriptionId = Read-Host (
        "Azure subscription ID [$($account.name): $($account.id)]"
    )
    $SubscriptionId = if ([string]::IsNullOrWhiteSpace($enteredSubscriptionId)) {
        [string]$account.id
    }
    else {
        $enteredSubscriptionId.Trim()
    }
    if ($SubscriptionId -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' -or
        [guid]$SubscriptionId -eq [guid]::Empty) {
        throw 'The entered Azure subscription ID must be a nonempty GUID.'
    }
}

if ($account.id -ine $SubscriptionId) {
    $null = Invoke-AzureCli -Arguments @(
        'account', 'set', '--subscription', $SubscriptionId, '--only-show-errors'
    )
    $account = Get-AzureJson -Arguments @('account', 'show')
}
if ($account.id -ine $SubscriptionId) {
    throw "Unable to select subscription '$SubscriptionId'."
}
Write-Host "Azure subscription '$($account.name)' ($SubscriptionId) validated." -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'ResourceGroupName must be supplied explicitly for noninteractive execution.'
    }
    $enteredResourceGroupName = Read-Host 'Resource group name [rg-HybridDeviceTagger]'
    $ResourceGroupName = if ([string]::IsNullOrWhiteSpace($enteredResourceGroupName)) {
        'rg-HybridDeviceTagger'
    }
    else {
        $enteredResourceGroupName.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($Location)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'Location must be supplied explicitly for noninteractive execution.'
    }
    $enteredLocation = Read-Host 'Azure location [westeurope]'
    $Location = if ([string]::IsNullOrWhiteSpace($enteredLocation)) {
        'westeurope'
    }
    else {
        $enteredLocation.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($FunctionAppName)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'FunctionAppName must be supplied explicitly for noninteractive execution.'
    }
    $defaultFunctionAppName = "func-devtagger-$([DateTime]::UtcNow.ToString('yyyyMMdd'))"
    $enteredFunctionAppName = Read-Host "Function App name [$defaultFunctionAppName]"
    $FunctionAppName = if ([string]::IsNullOrWhiteSpace($enteredFunctionAppName)) {
        $defaultFunctionAppName
    }
    else {
        $enteredFunctionAppName.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'StorageAccountName must be supplied explicitly for noninteractive execution.'
    }
    $storageNameSuffix = (Get-AppResourceNameSuffix) -replace '[^a-z0-9]', ''
    $maximumStorageSuffixLength = 22
    if ($storageNameSuffix.Length -gt $maximumStorageSuffixLength) {
        $storageNameSuffix = $storageNameSuffix.Substring(0, $maximumStorageSuffixLength)
    }
    $defaultStorageAccountName = "st$storageNameSuffix"
    $enteredStorageAccountName = Read-Host "Storage account name [$defaultStorageAccountName]"
    $StorageAccountName = if ([string]::IsNullOrWhiteSpace($enteredStorageAccountName)) {
        $defaultStorageAccountName
    }
    else {
        $enteredStorageAccountName.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($VirtualNetworkSubscriptionId)) {
    if (-not $virtualNetworkSubscriptionWasSupplied -and
        -not $virtualNetworkResourceGroupWasSupplied -and
        $env:TF_BUILD -ne 'True' -and $env:CI -ne 'true') {
        $enteredVirtualNetworkSubscriptionId = Read-Host (
            "Virtual network subscription ID [$SubscriptionId]"
        )
        $VirtualNetworkSubscriptionId = if (
            [string]::IsNullOrWhiteSpace($enteredVirtualNetworkSubscriptionId)
        ) {
            $SubscriptionId
        }
        else {
            $enteredVirtualNetworkSubscriptionId.Trim()
        }
        if ($VirtualNetworkSubscriptionId -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' -or
            [guid]$VirtualNetworkSubscriptionId -eq [guid]::Empty) {
            throw 'The entered virtual network subscription ID must be a nonempty GUID.'
        }
    }
    else {
        $VirtualNetworkSubscriptionId = $SubscriptionId
    }
}

do {
    if ([string]::IsNullOrWhiteSpace($VirtualNetworkResourceGroupName)) {
        $VirtualNetworkResourceGroupName = Read-RequiredInstallerValue `
            -Name 'VirtualNetworkResourceGroupName' `
            -Prompt 'Virtual network resource group name'
    }

    Write-Step 'Validating virtual network resource group'
    $virtualNetworkResourceGroup = Invoke-AzureCli -Arguments @(
        'group', 'show', '--subscription', $VirtualNetworkSubscriptionId,
        '--name', $VirtualNetworkResourceGroupName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($virtualNetworkResourceGroup.ExitCode -eq 0) {
        Write-Host "Virtual network resource group '$VirtualNetworkResourceGroupName' validated." -ForegroundColor Green
        break
    }

    $notFoundMessage = "Virtual network resource group '$VirtualNetworkResourceGroupName' was not found in subscription '$VirtualNetworkSubscriptionId'."
    if ($virtualNetworkResourceGroupWasSupplied) {
        $script:CompactInstallerError = $true
        throw $notFoundMessage
    }

    Write-Host $notFoundMessage -ForegroundColor Red
    $VirtualNetworkResourceGroupName = Read-RequiredInstallerValue `
        -Name 'VirtualNetworkResourceGroupName' `
        -Prompt 'Virtual network resource group name'
} while ($true)

do {
    if ([string]::IsNullOrWhiteSpace($VirtualNetworkName)) {
        $VirtualNetworkName = Read-RequiredInstallerValue `
            -Name 'VirtualNetworkName' `
            -Prompt 'Virtual network name'
    }

    Write-Step 'Validating existing virtual network'
    $virtualNetworkResult = Invoke-AzureCli -Arguments @(
        'network', 'vnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
        '--resource-group', $VirtualNetworkResourceGroupName, '--name', $VirtualNetworkName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($virtualNetworkResult.ExitCode -eq 0) {
        $virtualNetwork = $virtualNetworkResult.Output | ConvertFrom-Json -Depth 100
        if ($virtualNetwork.location -ine $Location) {
            $script:CompactInstallerError = $true
            throw "The selected virtual network '$VirtualNetworkName' is in '$($virtualNetwork.location)', but the Function App will be deployed to '$Location'. Azure Functions VNet integration requires both resources to use the same region. Select a VNet in '$Location' or restart the installer with -Location '$($virtualNetwork.location)'."
        }
        Write-Host "Virtual network '$VirtualNetworkName' validated." -ForegroundColor Green
        break
    }

    $notFoundMessage = "Virtual network '$VirtualNetworkName' was not found in resource group '$VirtualNetworkResourceGroupName'."
    if ($virtualNetworkNameWasSupplied) {
        $script:CompactInstallerError = $true
        throw $notFoundMessage
    }

    Write-Host $notFoundMessage -ForegroundColor Red
    $VirtualNetworkName = Read-RequiredInstallerValue `
        -Name 'VirtualNetworkName' `
        -Prompt 'Virtual network name'
} while ($true)

do {
    if ([string]::IsNullOrWhiteSpace($SubnetName)) {
        $SubnetName = Read-RequiredInstallerValue `
            -Name 'SubnetName' `
            -Prompt 'Flex Consumption integration subnet name'
    }

    Write-Step 'Validating FC1 integration subnet'
    $subnetResult = Invoke-AzureCli -Arguments @(
        'network', 'vnet', 'subnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
        '--resource-group', $VirtualNetworkResourceGroupName, '--vnet-name', $VirtualNetworkName,
        '--name', $SubnetName, '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($subnetResult.ExitCode -eq 0) {
        $subnet = $subnetResult.Output | ConvertFrom-Json -Depth 100
        try {
            Assert-Subnet -Subnet $subnet
        }
        catch {
            if ($subnetNameWasSupplied) {
                $script:CompactInstallerError = $true
                throw
            }

            Write-Host $_.Exception.Message -ForegroundColor Red
            $SubnetName = Read-RequiredInstallerValue `
                -Name 'SubnetName' `
                -Prompt 'Flex Consumption integration subnet name'
            continue
        }
        $script:ExpectedSubnetId = [string]$subnet.id
        Write-Host "FC1 integration subnet '$SubnetName' validated." -ForegroundColor Green
        break
    }

    $notFoundMessage = "FC1 integration subnet '$SubnetName' was not found in virtual network '$VirtualNetworkName'."
    if ($subnetNameWasSupplied) {
        $script:CompactInstallerError = $true
        throw $notFoundMessage
    }

    Write-Host $notFoundMessage -ForegroundColor Red
    $SubnetName = Read-RequiredInstallerValue `
        -Name 'SubnetName' `
        -Prompt 'Flex Consumption integration subnet name'
} while ($true)

do {
    if ([string]::IsNullOrWhiteSpace($PrivateEndpointSubnetName)) {
        $PrivateEndpointSubnetName = Read-RequiredInstallerValue `
            -Name 'PrivateEndpointSubnetName' `
            -Prompt 'Private endpoint subnet name'
    }

    Write-Step 'Validating private endpoint subnet'
    $privateEndpointSubnetResult = Invoke-AzureCli -Arguments @(
        'network', 'vnet', 'subnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
        '--resource-group', $VirtualNetworkResourceGroupName, '--vnet-name', $VirtualNetworkName,
        '--name', $PrivateEndpointSubnetName, '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($privateEndpointSubnetResult.ExitCode -eq 0) {
        $privateEndpointSubnet = $privateEndpointSubnetResult.Output |
            ConvertFrom-Json -Depth 100
        Assert-PrivateEndpointSubnet -Subnet $privateEndpointSubnet
        break
    }

    $notFoundMessage = "Private endpoint subnet '$PrivateEndpointSubnetName' was not found in virtual network '$VirtualNetworkName'."
    if ($privateEndpointSubnetNameWasSupplied) {
        $script:CompactInstallerError = $true
        throw $notFoundMessage
    }

    Write-Host $notFoundMessage -ForegroundColor Red
    $PrivateEndpointSubnetName = Read-RequiredInstallerValue `
        -Name 'PrivateEndpointSubnetName' `
        -Prompt 'Private endpoint subnet name'
} while ($true)

$privateEndpoints = Get-AzureJson -Arguments @(
    'network', 'private-endpoint', 'list', '--subscription', $VirtualNetworkSubscriptionId
)
if (@($privateEndpoints | Where-Object { $_.subnet.id -ieq $subnet.id }).Count -gt 0) {
    throw "FC1 integration subnet '$SubnetName' must not contain private endpoints."
}
Write-Host "Private endpoint subnet '$PrivateEndpointSubnetName' validated." -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($KeyVaultResourceGroupName)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'KeyVaultResourceGroupName must be supplied explicitly for noninteractive execution.'
    }
    $enteredKeyVaultResourceGroupName = Read-Host (
        "Key Vault resource group name [$ResourceGroupName]"
    )
    $KeyVaultResourceGroupName = if (
        [string]::IsNullOrWhiteSpace($enteredKeyVaultResourceGroupName)
    ) {
        $ResourceGroupName
    }
    else {
        $enteredKeyVaultResourceGroupName.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($KeyVaultName)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'KeyVaultName must be supplied explicitly for noninteractive execution.'
    }
    $keyVaultNameSuffix = ((Get-AppResourceNameSuffix) -replace '[^a-z0-9-]', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($keyVaultNameSuffix)) {
        $keyVaultNameSuffix = "devtagger-$([DateTime]::UtcNow.ToString('yyyyMMdd'))"
    }
    $maximumKeyVaultSuffixLength = 21
    if ($keyVaultNameSuffix.Length -gt $maximumKeyVaultSuffixLength) {
        $keyVaultNameSuffix = $keyVaultNameSuffix.Substring(0, $maximumKeyVaultSuffixLength).TrimEnd('-')
    }
    $defaultKeyVaultName = "kv-$keyVaultNameSuffix"
    $enteredKeyVaultName = Read-Host "Key Vault name [$defaultKeyVaultName]"
    $KeyVaultName = if ([string]::IsNullOrWhiteSpace($enteredKeyVaultName)) {
        $defaultKeyVaultName
    }
    else {
        $enteredKeyVaultName.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($ActiveDirectoryForestDomain)) {
    $defaultActiveDirectoryForestDomain = Get-LoggedOnUserForestDomain
    if ([string]::IsNullOrWhiteSpace($defaultActiveDirectoryForestDomain)) {
        $ActiveDirectoryForestDomain = Read-RequiredInstallerValue `
            -Name 'ActiveDirectoryForestDomain' `
            -Prompt 'Active Directory forest DNS name'
    }
    else {
        $enteredActiveDirectoryForestDomain = Read-Host (
            "Active Directory forest DNS name [$defaultActiveDirectoryForestDomain]"
        )
        $ActiveDirectoryForestDomain = if (
            [string]::IsNullOrWhiteSpace($enteredActiveDirectoryForestDomain)
        ) {
            $defaultActiveDirectoryForestDomain
        }
        else {
            $enteredActiveDirectoryForestDomain.Trim()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ActiveDirectoryServer)) {
    $ActiveDirectoryServer = $ActiveDirectoryForestDomain
}

if ([string]::IsNullOrWhiteSpace($ADServiceAccount)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'ADServiceAccount must be supplied explicitly for noninteractive execution.'
    }
    $defaultADServiceAccount = "HybridDeviceTagger@$ActiveDirectoryForestDomain"
    $enteredADServiceAccount = Read-Host (
        "Service Account [$defaultADServiceAccount]"
    )
    $ADServiceAccount = if (
        [string]::IsNullOrWhiteSpace($enteredADServiceAccount)
    ) {
        $defaultADServiceAccount
    }
    else {
        $enteredADServiceAccount.Trim()
    }
}

$requiredStringInputs = @()
if ([string]::IsNullOrWhiteSpace($ActiveDirectoryBindUserDistinguishedName)) {
    $resolvedServiceAccountDn = Resolve-ADServiceAccountDistinguishedName
    if ([string]::IsNullOrWhiteSpace($resolvedServiceAccountDn)) {
        $requiredStringInputs += @{
            Name = 'ActiveDirectoryBindUserDistinguishedName'
            Prompt = 'Active Directory bind user distinguished name'
        }
    }
    else {
        $ActiveDirectoryBindUserDistinguishedName = $resolvedServiceAccountDn
        Write-Host 'Existing Active Directory service account found; distinguished name resolved automatically.' -ForegroundColor Green
    }
}
if (-not $PrepareActiveDirectoryAccountOnly -and
    -not $SkipActiveDirectoryDirectoryChecks -and
    $null -eq $ActiveDirectoryCredential) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'ActiveDirectoryCredential must be supplied explicitly for noninteractive execution.'
    }
    $ActiveDirectoryCredential = Get-Credential `
        -UserName $ADServiceAccount `
        -Message 'Enter the Active Directory service-account password for bind validation'
    if ($null -eq $ActiveDirectoryCredential) {
        throw 'Active Directory service-account credential entry was canceled.'
    }
}
foreach ($inputDefinition in $requiredStringInputs) {
    $variable = Get-Variable -Name $inputDefinition.Name
    if ([string]::IsNullOrWhiteSpace([string]$variable.Value)) {
        $variable.Value = Read-RequiredInstallerValue `
            -Name $inputDefinition.Name `
            -Prompt $inputDefinition.Prompt
    }
}
if ($null -eq $TaggingRules -or $TaggingRules.Count -eq 0) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'TaggingRules must be supplied explicitly for noninteractive execution.'
    }
    Write-Step 'Creating tagging rule'
    $TaggingRules = @(Read-InteractiveTaggingRule)
}

if (-not $PrepareActiveDirectoryAccountOnly -and
    [string]::IsNullOrWhiteSpace($ManagementApiClientId)) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'ManagementApiClientId must be supplied explicitly for noninteractive execution.'
    }
    $ManagementApiClientId = Read-RequiredInstallerValue `
        -Name 'ManagementApiClientId' `
        -Prompt 'Management API Entra application (client) ID'
}
if (-not $PrepareActiveDirectoryAccountOnly -and
    $ManagementApiClientId -notmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    throw 'ManagementApiClientId must be a valid GUID.'
}
if (-not $PrepareActiveDirectoryAccountOnly -and
    ($null -eq $ManagementApiAllowedPrincipalObjectId -or
        $ManagementApiAllowedPrincipalObjectId.Count -eq 0)) {
    $signedInUserResult = Invoke-AzureCli -Arguments @(
        'ad', 'signed-in-user', 'show', '--query', '{ id: id }',
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    $signedInUser = if ($signedInUserResult.ExitCode -eq 0 -and
        -not [string]::IsNullOrWhiteSpace($signedInUserResult.Output)) {
        $signedInUserResult.Output | ConvertFrom-Json
    }
    else {
        $null
    }
    if ([guid]::TryParse([string]$signedInUser.id, [ref]([guid]$signedInUserId))) {
        $ManagementApiAllowedPrincipalObjectId = @($signedInUserId)
        Write-Host "Management API access restricted to signed-in Entra user '$signedInUserId'." -ForegroundColor Green
    }
    elseif ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'ManagementApiAllowedPrincipalObjectId must be supplied explicitly for noninteractive execution.'
    }
    else {
        $allowedPrincipalId = Read-RequiredInstallerValue `
            -Name 'ManagementApiAllowedPrincipalObjectId' `
            -Prompt 'Management API allowed Entra principal object ID'
        if (-not [guid]::TryParse($allowedPrincipalId, [ref]([guid]$parsedAllowedPrincipalId))) {
            throw 'ManagementApiAllowedPrincipalObjectId must be a valid GUID.'
        }
        $ManagementApiAllowedPrincipalObjectId = @($parsedAllowedPrincipalId)
    }
}

Write-Step 'Validating input parameters'
$requiredInputValidation = @(
    @{ Name = 'ResourceGroupName'; Value = $ResourceGroupName; MaximumLength = 90 },
    @{ Name = 'Location'; Value = $Location; MaximumLength = 64 },
    @{ Name = 'VirtualNetworkName'; Value = $VirtualNetworkName; MaximumLength = 64 },
    @{ Name = 'SubnetName'; Value = $SubnetName; MaximumLength = 80 },
    @{ Name = 'PrivateEndpointSubnetName'; Value = $PrivateEndpointSubnetName; MaximumLength = 80 },
    @{ Name = 'VirtualNetworkResourceGroupName'; Value = $VirtualNetworkResourceGroupName; MaximumLength = 90 },
    @{ Name = 'KeyVaultResourceGroupName'; Value = $KeyVaultResourceGroupName; MaximumLength = 90 },
    @{ Name = 'ActiveDirectoryServer'; Value = $ActiveDirectoryServer; MaximumLength = 253 },
    @{ Name = 'ADServiceAccount'; Value = $ADServiceAccount; MaximumLength = 256 },
    @{ Name = 'ActiveDirectoryBindUserDistinguishedName'; Value = $ActiveDirectoryBindUserDistinguishedName; MaximumLength = 2048 }
)
foreach ($validation in $requiredInputValidation) {
    if ([string]::IsNullOrWhiteSpace([string]$validation.Value) -or
        ([string]$validation.Value).Length -gt $validation.MaximumLength) {
        throw "$($validation.Name) must contain 1-$($validation.MaximumLength) characters."
    }
}
if ($FunctionAppName -notmatch '^[a-zA-Z0-9-]{2,60}$') {
    throw 'FunctionAppName must contain 2-60 letters, numbers, or hyphens.'
}
if ($StorageAccountName -notmatch '^[a-z0-9]{3,24}$') {
    throw 'StorageAccountName must contain 3-24 lowercase letters or numbers.'
}
if ($KeyVaultName -notmatch '^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$') {
    throw 'KeyVaultName must contain 3-24 letters, numbers, or hyphens and must start with a letter and end with a letter or number.'
}
if ($ActiveDirectoryForestDomain -notmatch '^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$') {
    throw 'ActiveDirectoryForestDomain must be a valid DNS domain name.'
}
if ($TaggingRules.Count -gt 100) {
    throw 'TaggingRules cannot contain more than 100 rules.'
}
$script:NormalizedTaggingRules = @(ConvertTo-NormalizedTaggingRule)
$normalizedTaggingRulesJson = ConvertTo-Json -InputObject $script:NormalizedTaggingRules -Depth 10 -Compress
if ($normalizedTaggingRulesJson.Length -gt 32768) {
    throw 'Serialized tagging configuration cannot exceed 32768 characters.'
}

foreach ($tag in $Tags.GetEnumerator()) {
    if ([string]::IsNullOrEmpty([string]$tag.Key) -or ([string]$tag.Key).Length -gt 512) {
        throw 'Azure tag keys must contain 1-512 characters.'
    }
    if (([string]$tag.Value).Length -gt 256) {
        throw "Azure tag '$($tag.Key)' value cannot exceed 256 characters."
    }
}

Write-Step 'Checking local prerequisites'
Assert-Command -Name dotnet -InstallHint 'Install the .NET 8 SDK.'
if (-not (Test-Path $templatePath)) { throw "Bicep template not found: $templatePath" }
if (-not (Test-Path $projectPath)) { throw "Function project not found: $projectPath" }
if ($ActiveDirectoryTrustedCertificatePath -and -not (Test-Path -LiteralPath $ActiveDirectoryTrustedCertificatePath -PathType Leaf)) {
    throw "Active Directory trusted certificate not found: $ActiveDirectoryTrustedCertificatePath"
}
if ($ActiveDirectoryTrustedCertificatePath) {
    try {
        $script:TrustedCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            (Resolve-Path -LiteralPath $ActiveDirectoryTrustedCertificatePath).Path)
    }
    catch {
        throw "Active Directory trusted certificate is not a valid X.509 certificate: $ActiveDirectoryTrustedCertificatePath"
    }
    if ($script:TrustedCertificate.NotAfter -le [DateTime]::UtcNow) {
        throw "Active Directory trusted certificate has expired: $ActiveDirectoryTrustedCertificatePath"
    }
    Write-Host "Using Active Directory trust anchor $($script:TrustedCertificate.Thumbprint)."
}
$script:ActiveDirectoryPasswordChanged = $false
$script:EffectiveAllowLdapFallback = $false

$azVersion = Get-AzureJson -Arguments @('version')
if ([version]$azVersion.'azure-cli' -lt [version]'2.60.0') {
    throw "Azure CLI 2.60.0 or newer is required; installed version is $($azVersion.'azure-cli')."
}
$sdkVersions = & dotnet --list-sdks
if (-not ($sdkVersions -match '^8\.')) {
    throw 'A .NET 8 SDK is required.'
}

if ([string]::IsNullOrWhiteSpace($VirtualNetworkSubscriptionId)) {
    $VirtualNetworkSubscriptionId = $SubscriptionId
}
if ([string]::IsNullOrWhiteSpace($KeyVaultSubscriptionId)) {
    $KeyVaultSubscriptionId = $SubscriptionId
}

Write-Step 'Validating Entra administrative-unit targets'
$restrictedAdministrativeUnits = @(Get-RestrictedAdministrativeUnitTarget)
if ($restrictedAdministrativeUnits.Count -gt 0) {
    $targetSummary = $restrictedAdministrativeUnits |
        ForEach-Object { "  - $($_.displayName) ($($_.id))" } |
        Out-String
    Write-Warning "Rules target restricted management administrative units:`n$targetSummary"
}

Write-InstallationSummary -RestrictedAdministrativeUnits $restrictedAdministrativeUnits
if (-not $ValidateOnly -and -not $WhatIfPreference) {
    if (-not $PSCmdlet.ShouldProcess(
        "$SubscriptionId/$ResourceGroupName",
        'Execute the installation plan shown above'
    )) {
        Write-Host 'Installation canceled before any changes were made.' -ForegroundColor Yellow
        return
    }
}

if ($restrictedAdministrativeUnits.Count -gt 0) {
    if (-not $ValidateOnly -and -not $WhatIfPreference -and
        -not $GrantRestrictedAdministrativeUnitRole) {
        if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
            throw 'RMAU targets require explicit approval. Re-run with -GrantRestrictedAdministrativeUnitRole.'
        }
        $confirmation = Read-Host 'Create and assign the least-privilege device extension-attribute role at these RMAU scopes? Type YES to continue'
        if ($confirmation -cne 'YES') {
            throw 'RMAU role assignment was not explicitly approved; installation stopped before deployment.'
        }
    }
}

Write-Step 'Checking Azure resource providers'
foreach ($provider in $requiredProviders) {
    $state = (Invoke-AzureCli -Arguments @(
        'provider', 'show', '--namespace', $provider,
        '--subscription', $SubscriptionId,
        '--query', 'registrationState', '--output', 'tsv', '--only-show-errors'
    )).Output.Trim()
    if ($state -ne 'Registered') {
        if ($RegisterProviders -and -not $ValidateOnly -and -not $WhatIfPreference) {
            $null = Invoke-AzureCli -Arguments @(
                'provider', 'register', '--namespace', $provider,
                '--subscription', $SubscriptionId, '--wait', '--only-show-errors'
            )
        }
        else {
            throw "Provider '$provider' is '$state'. Re-run an installation with -RegisterProviders."
        }
    }
}

Write-Step 'Validating resource group and FC1 region'
$resourceGroupResult = Invoke-AzureCli -Arguments @(
    'group', 'show', '--subscription', $SubscriptionId,
    '--name', $ResourceGroupName, '--only-show-errors', '--output', 'json'
) -AllowFailure
if ($resourceGroupResult.ExitCode -ne 0) {
    if (-not $CreateResourceGroup) {
        throw "Resource group '$ResourceGroupName' does not exist. Use -CreateResourceGroup for installation."
    }
    if ($ValidateOnly -or $WhatIfPreference) {
        throw "Resource group '$ResourceGroupName' must exist before validation or What-if can run."
    }
    $null = Invoke-AzureCli -Arguments @(
        'group', 'create', '--subscription', $SubscriptionId,
        '--name', $ResourceGroupName, '--location', $Location,
        '--only-show-errors'
    )
}
$fc1Locations = Get-AzureJson -Arguments @('functionapp', 'list-flexconsumption-locations')
if (@($fc1Locations | Where-Object { $_.name -ieq $Location }).Count -eq 0) {
    throw "Location '$Location' does not support Azure Functions Flex Consumption."
}

Write-Step 'Validating existing Key Vault and password secret'
$vault = Get-AzureJson -Arguments @(
    'keyvault', 'show', '--subscription', $KeyVaultSubscriptionId,
    '--resource-group', $KeyVaultResourceGroupName, '--name', $KeyVaultName
)
$vaultConfiguration = if ($null -ne $vault.PSObject.Properties['properties']) {
    $vault.properties
}
else {
    $vault
}
Assert-KeyVault -Vault $vaultConfiguration -SkipCurrentCredential:$PrepareActiveDirectoryAccountOnly

Write-Step 'Validating existing app-owned resources and global names'
Assert-ExistingOwnedResource

if ($PrepareActiveDirectoryAccountOnly) {
    Write-Step 'Preparing Active Directory service account'
    Initialize-ActiveDirectoryServiceAccount
    if (-not $script:ActiveDirectoryPasswordChanged) {
        throw 'Active Directory account preparation did not set a password.'
    }
    Set-CurrentPasswordSecret -VaultUri $vaultConfiguration.vaultUri
    Assert-KeyVault -Vault $vaultConfiguration
}

Write-Step 'Validating Active Directory forest, OU, account, read access, and LDAPS certificate'
if ($SkipActiveDirectoryDirectoryChecks) {
    if ($PrepareActiveDirectoryAccountOnly) {
        throw 'Active Directory directory checks cannot be skipped while preparing the service account.'
    }
    if (-not $script:TrustedCertificate) {
        throw 'Skipping Active Directory directory checks requires an explicit trusted certificate.'
    }
    $existingFunction = Invoke-AzureCli -Arguments @(
        'functionapp', 'show',
        '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName,
        '--name', $FunctionAppName,
        '--only-show-errors', '--output', 'none'
    ) -AllowFailure
    if ($existingFunction.ExitCode -ne 0) {
        throw 'Active Directory directory checks may be skipped only when updating an existing Function App.'
    }
    if (-not [HybridDeviceTaggerLdapCertificateValidatorV2]::ValidateTlsEndpoint(
        $ActiveDirectoryServer,
        636,
        $script:TrustedCertificate)) {
        throw "Strict LDAPS certificate validation failed for '$ActiveDirectoryServer:636'."
    }
    $script:EffectiveAllowLdapFallback = $false
    Write-Warning 'Skipped repeated forest, OU, and account checks for the existing deployment; strict LDAPS endpoint validation succeeded.'
}
else {
    Test-LdapConfiguration
}

if ($PrepareActiveDirectoryAccountOnly) {
    Write-Host 'Active Directory service-account preparation completed. No Azure resources were deployed.' -ForegroundColor Green
    return
}

$parameterFile = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$([guid]::NewGuid()).parameters.json"
try {
    $parameterJson = Get-DeploymentParameter | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $parameterFile,
        $parameterJson,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Step 'Validating Bicep deployment'
    $null = Invoke-AzureCli -Arguments @(
        'deployment', 'group', 'validate', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--template-file', $templatePath,
        '--parameters', "@$parameterFile", '--only-show-errors'
    )

    if ($ValidateOnly) {
        Write-Host "Validation completed successfully. No resources were changed." -ForegroundColor Green
        return
    }

    if ($WhatIfPreference) {
        Write-Step 'Running Azure Resource Manager What-if'
        $whatIfResult = Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'what-if', '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName, '--template-file', $templatePath,
            '--parameters', "@$parameterFile", '--result-format', 'FullResourcePayloads'
        )
        if (-not [string]::IsNullOrWhiteSpace($whatIfResult.Output)) {
            Write-Host $whatIfResult.Output
        }
        Write-Host 'What-if completed. No resources were changed.' -ForegroundColor Green
        return
    }

    Write-Step 'Deploying Azure infrastructure'
    $deploymentName = "hybrid-device-tagger-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
    $deployment = Get-AzureJson -Arguments @(
        'deployment', 'group', 'create', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $deploymentName,
        '--template-file', $templatePath, '--parameters', "@$parameterFile"
    )
    $principalId = [string]$deployment.properties.outputs.functionAppPrincipalId.value
    if ([string]::IsNullOrWhiteSpace($principalId)) {
        throw 'The deployment did not return the Function App managed identity principal ID.'
    }

    Write-Step 'Assigning Microsoft Graph application permissions'
    Grant-GraphApplicationPermission -PrincipalId $principalId

    if ($restrictedAdministrativeUnits.Count -gt 0) {
        Write-Step 'Assigning restricted administrative-unit roles'
        Grant-RestrictedAdministrativeUnitRole `
            -PrincipalId $principalId `
            -AdministrativeUnits $restrictedAdministrativeUnits
    }

    if (-not $SkipCodeDeployment) {
        Write-Step 'Building and deploying Function App code'
        Publish-FunctionApp
    }

    Write-Host "Installation completed for Function App '$FunctionAppName'." -ForegroundColor Green
}
finally {
    if ([System.IO.File]::Exists($parameterFile)) {
        [System.IO.File]::Delete($parameterFile)
    }
}
}
catch {
    if ($VerbosePreference -eq 'Continue' -or -not $script:CompactInstallerError) {
        Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
    $compactFailure = $true
    $Host.UI.WriteErrorLine($_.Exception.Message)
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
    if (-not $compactFailure) {
        Write-Host "Installer log: $installerLogPath"
    }
}
