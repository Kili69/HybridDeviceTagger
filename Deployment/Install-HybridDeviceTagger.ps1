#Requires -Version 7.2
#Requires -Modules ActiveDirectory
# Project-Version: 0.1.20261006.2
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
.SYNOPSIS
Validates and deploys HybridDeviceTagger to an Azure Functions Flex Consumption environment.

.DESCRIPTION
Validates local prerequisites, the selected Azure account, existing networking, the selected Key
Vault mode, Active Directory connectivity, tagging rules, and the Bicep deployment. Depending on
the selected switches, the script can prepare the Active Directory service account, run a
read-only validation, show Azure What-If output, deploy infrastructure, assign Microsoft Graph
application roles, and publish the .NET Function App.

The installer references existing virtual networks and subnets without modifying those shared
resources. It can reference an existing Key Vault or create a private vault in the deployment
resource group. It uses the supplied Active Directory credentials only in the
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
Name of the selected Key Vault that stores the current and staged AD passwords. Interactive runs
derive the default from FunctionAppName using a kv prefix and the Function App name suffix.

.PARAMETER KeyVaultResourceGroupName
Resource group containing an existing Key Vault. New vaults always use ResourceGroupName.

.PARAMETER CreateKeyVault
Creates a private Key Vault in the Function App resource group and subscription. When omitted,
interactive runs ask whether to create a vault; noninteractive runs use an existing vault.

.PARAMETER ActiveDirectoryForestDomain
DNS name of the Active Directory forest. Interactive runs propose the forest of the logged-on
Active Directory user when it can be detected. Workgroup systems have no default.

.PARAMETER ActiveDirectoryServer
Optional DNS name of a domain controller used only for installer validation. When omitted, the
installer discovers a controller through _ldap._tcp.dc._msdcs DNS SRV records. The deployed
Function always performs its own DNS SRV discovery.

.PARAMETER AllowLdapFallback
Retained for command-line compatibility. Secure LDAP fallback now uses StartTLS automatically and
never performs an unencrypted LDAP bind.

.PARAMETER LdapFallbackPort
LDAP fallback port. The default is 389.

.PARAMETER ActiveDirectoryTrustedCertificatePath
Optional path to a public root CA or self-signed lab DC certificate used as the LDAPS trust anchor.
Private keys are rejected and never packaged.

.PARAMETER ADServiceAccount
UPN used by the Function App to bind to Active Directory. This parameter cannot be combined with
ADServiceAccountCredential. Interactive runs without either value default to
HybridDeviceTagger@ActiveDirectoryForestDomain and request the password separately.

.PARAMETER ActiveDirectoryBindUserDistinguishedName
Optional distinguished name of the AD service account used for password changes. When omitted, the
installer resolves it from ADServiceAccount and stops if the account cannot be resolved uniquely.

.PARAMETER ADServiceAccountCredential
Credential used locally to validate directory bind and read access. Interactive runs request the
AD service-account password when the credential is omitted and directory checks are enabled. Its
UserName supplies the service-account UPN. This parameter cannot be combined with ADServiceAccount.

.PARAMETER ManagementApiClientId
Application ID of the single-tenant Entra registration that protects the management API. When
omitted for a new Function App, the installer creates and configures the registration
automatically. Updates reuse the client ID stored in the existing Function App configuration.

.PARAMETER ManagementApiAllowedPrincipalObjectId
Bootstrap users, groups, or service principals that receive all five management API roles unless a
role-specific principal list is supplied. This preserves existing installations during migration.

.PARAMETER ManagementApiReaderPrincipalObjectId
Users, groups, or service principals allowed to read all rules, statuses, and traces.

.PARAMETER ManagementApiRuleAdministratorPrincipalObjectId
Users, groups, or service principals allowed to read and change tagging rules.

.PARAMETER ManagementApiTaggingOperatorPrincipalObjectId
Users, groups, or service principals allowed to read tagging diagnostics and start tagging runs.

.PARAMETER ManagementApiPasswordRotationAdministratorPrincipalObjectId
Users, groups, or service principals allowed to read password-rotation status and force rotation.

.PARAMETER ManagementApiCertificateAdministratorPrincipalObjectId
Users, groups, or service principals allowed to read and change Active Directory LDAPS trust certificates.

.PARAMETER VirtualNetworkSubscriptionId
Subscription containing the existing virtual network. Interactive runs propose SubscriptionId,
which can be accepted by pressing Enter.

.PARAMETER KeyVaultSubscriptionId
Subscription containing an existing Key Vault. New vaults always use SubscriptionId.

.PARAMETER CurrentCredentialEntryName
Name of the Key Vault secret containing the active AD password.

.PARAMETER StagedCredentialEntryName
Name of the Key Vault secret used while rotating or recovering the AD password.

.PARAMETER TaggingSchedule
Six-field NCRONTAB schedule for device reconciliation. The default runs every 15 minutes.

.PARAMETER RotationSchedule
Six-field NCRONTAB schedule for password-rotation checks. The default runs daily at 02:30 UTC.

.PARAMETER MaximumInstanceCount
Maximum Flex Consumption scale-out instance count.

.PARAMETER InstanceMemoryMB
Memory allocated to each FC1 instance. Valid values are 512, 2048, and 4096 MB.

.PARAMETER Tags
Azure resource tags applied to app-owned resources.

.PARAMETER CreateResourceGroup
Creates the target resource group when it does not already exist.

.PARAMETER RegisterProviders
Registers missing Azure resource providers required by the deployment.

.PARAMETER Force
Runs without interactive input or installation confirmation. Every required value and explicit
consent switch must be supplied; missing information terminates the run with a parameter error.

.PARAMETER SkipActiveDirectoryDirectoryChecks
Skips repeated forest, OU, and service-account directory checks for an existing installation.
Strict endpoint and certificate validation still runs.

.PARAMETER ValidateOnly
Runs all supported prerequisite and configuration validation without changing resources.

.PARAMETER SkipCodeDeployment
Deploys or validates infrastructure and permissions without publishing Function App code.

.PARAMETER FunctionPackagePath
Path to the prebuilt Function App ZIP included with the installation package. Defaults to
HybridDeviceTagger.zip next to this installer.

.EXAMPLE
$credential = Get-Credential 'svc-HybridDeviceTagger@contoso.com'
./Deployment/Install-HybridDeviceTagger.ps1 @parameters `
    -ADServiceAccountCredential $credential `
    -ValidateOnly

Runs the complete read-only preflight using values collected in the parameters splat.

.EXAMPLE
./Deployment/Install-HybridDeviceTagger.ps1 @parameters -Force

Deploys the validated infrastructure and Function package without interactive prompts. Run
ValidateOnly and WhatIf first.

.NOTES
Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>

Installer log: $env:TEMP\HybridDeviceTagger-Install.log

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
    Justification = 'Internal mutations are guarded by script-level approval, explicitly forced, or skipped for ValidateOnly and WhatIf.'
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
    [pscredential]$ADServiceAccountCredential,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$ManagementApiClientId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiAllowedPrincipalObjectId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiReaderPrincipalObjectId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiRuleAdministratorPrincipalObjectId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiTaggingOperatorPrincipalObjectId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiPasswordRotationAdministratorPrincipalObjectId,

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [guid[]]$ManagementApiCertificateAdministratorPrincipalObjectId,

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

    [ValidateCount(0, 50)]
    [hashtable]$Tags = @{},

    [switch]$CreateResourceGroup,
    [switch]$CreateKeyVault,
    [switch]$RegisterProviders,
    [switch]$Force,
    [switch]$SkipActiveDirectoryDirectoryChecks,
    [switch]$ValidateOnly,
    [switch]$SkipCodeDeployment,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 4096)]
    [string]$FunctionPackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:TrustedCertificate = $null
$script:CompactInstallerError = $false
$script:CreateManagementApiApplication = $false
$script:CreatedManagementApiApplicationObjectId = $null
$script:AzureDeploymentStarted = $false
$script:NonInteractive = $Force -or $env:TF_BUILD -eq 'True' -or $env:CI -eq 'true'
if ($PSBoundParameters.ContainsKey('ADServiceAccount') -and
    $PSBoundParameters.ContainsKey('ADServiceAccountCredential')) {
    $message = @'
The Active Directory service account was specified twice.

Choose exactly one of these options:
  - Use -ADServiceAccountCredential $credential to provide the UPN and password together.
  - Use -ADServiceAccount 'service-account@contoso.com' to enter the password interactively.

Remove either -ADServiceAccount or -ADServiceAccountCredential and run the installer again.
'@
    $errorRecord = [System.Management.Automation.ErrorRecord]::new(
        [ArgumentException]::new($message),
        'ConflictingADServiceAccountParameters',
        [System.Management.Automation.ErrorCategory]::InvalidArgument,
        $null
    )
    $PSCmdlet.ThrowTerminatingError($errorRecord)
}
$createKeyVaultWasSupplied = $PSBoundParameters.ContainsKey('CreateKeyVault')
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
$templatePath = Join-Path $scriptRoot 'main.bicep'
if ([string]::IsNullOrWhiteSpace($FunctionPackagePath)) {
    $FunctionPackagePath = Join-Path $scriptRoot 'HybridDeviceTagger.zip'
}
$trustedCertificatePackagePath = 'certificates/active-directory-trust-anchor.pem'
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
if (-not ('HybridDeviceTaggerLdapCertificateValidatorV4' -as [type])) {
    Add-Type -ReferencedAssemblies $certificateAssemblies -TypeDefinition @'
using System;
using System.DirectoryServices.Protocols;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable SYSLIB0057

public static class HybridDeviceTaggerLdapCertificateValidatorV4
{
    public static VerifyServerCertificateCallback Create(
        string expectedServer,
        X509Certificate2 trustAnchor)
    {
        return (_, certificate) => Validate(expectedServer, certificate, trustAnchor);
    }

    public static RemoteCertificateValidationCallback CreateRemote(
        string expectedServer,
        X509Certificate2 trustAnchor)
    {
        return (_, certificate, _, _) =>
            certificate is not null && Validate(expectedServer, certificate, trustAnchor);
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
        var certificateName = serverCertificate.GetNameInfo(X509NameType.DnsName, false);
        if (!certificateName.Equals(expectedServer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var trustChain = CreateChain(trustAnchor, X509RevocationMode.NoCheck);
        if (!trustChain.Build(serverCertificate))
        {
            return false;
        }
        if (trustChain.ChainElements.Count == 1)
        {
            return true;
        }

        using var revocationChain = CreateChain(trustAnchor, X509RevocationMode.Online);
        return revocationChain.Build(serverCertificate);
    }

    private static X509Chain CreateChain(
        X509Certificate2 trustAnchor,
        X509RevocationMode revocationMode)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustAnchor);
        chain.ChainPolicy.RevocationMode = revocationMode;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain;
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
    else {
        'Azure infrastructure and Function App deployment'
    }

    Write-Host "Mode: $mode"
    Write-Host "Azure subscription: $SubscriptionId"
    Write-Host "Target resource group: $ResourceGroupName"
    Write-Host "Azure location: $Location"

    Write-Host 'Planned Azure resources to create or update:'
    Write-Host "  - Function App: $FunctionAppName (.NET 8 isolated, Flex Consumption FC1)"
    Write-Host "  - Storage account: $StorageAccountName with deployment, membership-ledger, and run-traces containers"
    Write-Host "  - Flex Consumption plan: $FunctionAppName-plan"
    Write-Host "  - Log Analytics workspace: $FunctionAppName-law"
    Write-Host "  - Application Insights: $FunctionAppName-appi"
    Write-Host "  - Storage private endpoint: pe-$StorageAccountName-blob"
    if ($script:StoragePrivateDnsZoneResourceId) {
        Write-Host "  - Reuse Storage private DNS zone: $($script:StoragePrivateDnsZoneResourceId)"
    }
    else {
        Write-Host '  - Storage private DNS zone and virtual-network link'
    }
    if ($script:CreateManagementApiApplication) {
        Write-Host '  - Create a single-tenant Entra application and Enterprise Application for the management API'
    }
    else {
        Write-Host "  - Entra-authenticated management API: api://$ManagementApiClientId"
    }
    Write-Host '  - Five least-privilege Entra application roles plus Function App Owner/Contributor authorization'
    Write-Host '  - Managed-identity role assignments for Storage and Key Vault'
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
    if ($CreateKeyVault) {
        Write-Host "  - Private Key Vault: $KeyVaultName"
        Write-Host "  - Key Vault private endpoint: pe-$KeyVaultName"
        Write-Host '  - Key Vault private DNS zone and virtual-network link'
    }
    if ($SkipCodeDeployment) {
        Write-Host '  - Function App code deployment is skipped.'
    }
    else {
        Write-Host '  - Deploy the prebuilt Function App code package'
    }
    Write-Host '  - Initialize the AD credential through the Function managed identity'

    Write-Host 'Existing shared resources referenced without creation:'
    Write-Host "  - Virtual network: $VirtualNetworkSubscriptionId/$VirtualNetworkResourceGroupName/$VirtualNetworkName"
    Write-Host "  - Integration subnet: $SubnetName"
    Write-Host "  - Private endpoint subnet: $PrivateEndpointSubnetName"
    if (-not $CreateKeyVault) {
        Write-Host "  - Key Vault: $KeyVaultSubscriptionId/$KeyVaultResourceGroupName/$KeyVaultName"
    }
    Write-Host '  - The Active Directory service account is not changed during normal deployment.'
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure,
        [ValidateRange(1, 3600)]
        [int]$TimeoutSeconds = 900
    )

    $azureCliCommand = Get-Command az -ErrorAction Stop
    $commandArguments = [Collections.Generic.List[string]]::new()
    if ([IO.Path]::GetExtension($azureCliCommand.Source) -ieq '.cmd') {
        $executablePath = [IO.Path]::GetFullPath(
            (Join-Path (Split-Path $azureCliCommand.Source) '..\python.exe')
        )
        if (-not [IO.File]::Exists($executablePath)) {
            throw "Azure CLI Python executable was not found: $executablePath"
        }
        $commandArguments.Add('-IBm')
        $commandArguments.Add('azure.cli')
    }
    else {
        $executablePath = $azureCliCommand.Source
    }
    foreach ($argument in $Arguments) {
        $commandArguments.Add($argument)
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $commandArguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Azure CLI process could not be started: $executablePath"
        }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Azure CLI timed out after $TimeoutSeconds seconds (az $($Arguments -join ' '))."
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
    $text = @($standardOutput, $standardError).
        Where({ -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed (az $($Arguments -join ' ')):`n$text"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Get-AzureJson {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [ValidateRange(1, 3600)]
        [int]$TimeoutSeconds = 900
    )
    $result = Invoke-AzureCli `
        -Arguments ($Arguments + @('--only-show-errors', '--output', 'json')) `
        -TimeoutSeconds $TimeoutSeconds
    if ([string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }
    $result.Output | ConvertFrom-Json -Depth 100
}

function Wait-AzureGroupDeployment {
    param(
        [Parameter(Mandatory)][string]$DeploymentName,
        [ValidateRange(60, 7200)]
        [int]$TimeoutSeconds = 1800
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastReportedState = $null
    $lastProgressReport = [DateTimeOffset]::MinValue
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $deployment = Get-AzureJson -Arguments @(
            'deployment', 'group', 'show',
            '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName,
            '--name', $DeploymentName
        ) -TimeoutSeconds 60
        $state = [string]$deployment.properties.provisioningState
        $now = [DateTimeOffset]::UtcNow
        if ($state -ne $lastReportedState -or
            ($now - $lastProgressReport).TotalSeconds -ge 30) {
            Write-Host "Azure deployment '$DeploymentName' state: $state"
            $lastReportedState = $state
            $lastProgressReport = $now
        }

        if ($state -eq 'Succeeded') {
            return $deployment
        }
        if ($state -in 'Failed', 'Canceled') {
            $errorText = if ($null -ne $deployment.properties.error) {
                $deployment.properties.error | ConvertTo-Json -Depth 20 -Compress
            }
            else {
                'Azure Resource Manager did not return deployment error details.'
            }
            throw "Azure deployment '$DeploymentName' ended with state '$state': $errorText"
        }
        Start-Sleep -Seconds 10
    }

    throw "Azure deployment '$DeploymentName' is still running after $TimeoutSeconds seconds. Check its status in Azure before retrying; do not start a concurrent deployment with the same resources."
}

function Start-AzureGroupDeployment {
    param(
        [Parameter(Mandatory)][string]$DeploymentName,
        [Parameter(Mandatory)][string]$TemplateFile,
        [Parameter(Mandatory)][string]$ParameterFile
    )

    $template = Get-Content -LiteralPath $TemplateFile -Raw |
        ConvertFrom-Json -Depth 100
    $parameterDocument = Get-Content -LiteralPath $ParameterFile -Raw |
        ConvertFrom-Json -Depth 100
    $body = @{
        properties = @{
            mode = 'Incremental'
            template = $template
            parameters = $parameterDocument.parameters
        }
    } | ConvertTo-Json -Depth 100 -Compress
    $tokenResult = Get-AzureJson -Arguments @(
        'account', 'get-access-token',
        '--subscription', $SubscriptionId,
        '--resource', 'https://management.azure.com/'
    ) -TimeoutSeconds 60
    if ([string]::IsNullOrWhiteSpace([string]$tokenResult.accessToken)) {
        throw 'Azure CLI did not return an Azure Resource Manager access token.'
    }

    $secureToken = [Security.SecureString]::new()
    foreach ($character in ([string]$tokenResult.accessToken).ToCharArray()) {
        $secureToken.AppendChar($character)
    }
    $secureToken.MakeReadOnly()
    $tokenResult = $null
    $escapedResourceGroup = [Uri]::EscapeDataString($ResourceGroupName)
    $escapedDeploymentName = [Uri]::EscapeDataString($DeploymentName)
    $uri = (
        "https://management.azure.com/subscriptions/$SubscriptionId/" +
        "resourcegroups/$escapedResourceGroup/providers/Microsoft.Resources/" +
        "deployments/${escapedDeploymentName}?api-version=2022-09-01"
    )
    try {
        Invoke-RestMethod `
            -Method Put `
            -Uri $uri `
            -Authentication Bearer `
            -Token $secureToken `
            -ContentType 'application/json' `
            -Body $body `
            -TimeoutSec 60
    }
    finally {
        $secureToken.Dispose()
        $body = $null
        $tokenResult = $null
    }
}

function Get-PrivateDnsZoneResourceId {
    param([Parameter(Mandatory)][string]$ZoneName)

    $zones = @(Get-AzureJson -Arguments @(
        'network', 'private-dns', 'zone', 'list',
        '--subscription', $VirtualNetworkSubscriptionId,
        '--query', "[?name=='$ZoneName'].{id:id,name:name,resourceGroup:resourceGroup}"
    ))
    foreach ($zone in $zones) {
        $links = @(Get-AzureJson -Arguments @(
            'network', 'private-dns', 'link', 'vnet', 'list',
            '--subscription', $VirtualNetworkSubscriptionId,
            '--resource-group', [string]$zone.resourceGroup,
            '--zone-name', [string]$zone.name
        ))
        if (@($links | Where-Object {
            [string]$_.virtualNetwork.id -ieq $script:ExpectedVirtualNetworkId
        }).Count -gt 0) {
            return [string]$zone.id
        }
    }

    ''
}

function ConvertTo-AzureCliJsonArgument {
    param([Parameter(Mandatory)][string]$Json)

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

    if ($script:NonInteractive) {
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
    $connection = $null
    try {
        $connection = Connect-NextAvailableLdapDirectory `
            -Servers $script:ActiveDirectoryServerCandidates `
            -Domain $ActiveDirectoryForestDomain
        $entries = @(Invoke-LdapSearch `
            -Connection $connection `
            -BaseDn (Convert-DnsNameToDistinguishedName $ActiveDirectoryForestDomain) `
            -Filter "(&(objectClass=user)(userPrincipalName=$escapedServiceAccount))" `
            -Scope ([Novell.Directory.Ldap.LdapConnection]::ScopeSub) `
            -Attributes @('distinguishedName'))
        if ($entries.Count -gt 1) {
            throw "More than one Active Directory user has the UPN '$ADServiceAccount'."
        }
        if ($entries.Count -eq 1) {
            return [string]$entries[0].Dn
        }
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }

    $null
}

function Convert-DnsNameToDistinguishedName {
    param([Parameter(Mandatory)][string]$DnsName)
    ($DnsName.Trim('.').Split('.') | ForEach-Object { "DC=$_" }) -join ','
}

function Get-DomainDnsNameFromDistinguishedName {
    param([Parameter(Mandatory)][string]$DistinguishedName)
    @([regex]::Matches(
        $DistinguishedName,
        '(?i)(?:^|(?<!\\),)\s*DC\s*=\s*([^,]+)'
    ) | ForEach-Object { $_.Groups[1].Value.Trim() }) -join '.'
}

function Connect-LdapDirectory {
    param(
        [Parameter(Mandatory)][string]$Server,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][bool]$UseSsl,
        [Parameter()][bool]$UseStartTls = $false,
        [Parameter()][pscredential]$Credential = $ADServiceAccountCredential
    )

    if ($UseSsl -and $UseStartTls) {
        throw 'LDAPS and StartTLS cannot be enabled on the same LDAP connection.'
    }
    $networkCredential = $Credential.GetNetworkCredential()
    $options = [Novell.Directory.Ldap.LdapConnectionOptions]::new()
    if ($script:TrustedCertificate) {
        $options = $options.ConfigureRemoteCertificateValidationCallback(
            [HybridDeviceTaggerLdapCertificateValidatorV4]::CreateRemote(
                $Server,
                $script:TrustedCertificate))
    }
    if ($UseSsl) {
        $options = $options.UseSsl()
    }
    $connection = [Novell.Directory.Ldap.LdapConnection]::new($options)
    $connection.ConnectionTimeout = 15000

    try {
        $null = $connection.ConnectAsync(
            $Server,
            $Port,
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        if ($UseStartTls) {
            $null = $connection.StartTlsAsync(
                [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        }
        $null = $connection.BindAsync(
            $networkCredential.UserName,
            $networkCredential.Password,
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $connection
    }
    catch {
        $connection.Dispose()
        $exceptionMessages = [Collections.Generic.List[string]]::new()
        $ldapResultCode = $null
        $ldapDiagnostic = $null
        $currentException = $_.Exception
        while ($null -ne $currentException) {
            if (-not [string]::IsNullOrWhiteSpace($currentException.Message)) {
                $exceptionMessages.Add($currentException.Message)
            }
            $resultCodeProperty = $currentException.PSObject.Properties['ResultCode']
            if ($null -ne $resultCodeProperty) {
                $ldapResultCode = [int]$resultCodeProperty.Value
            }
            $ldapErrorProperty = $currentException.PSObject.Properties['LdapErrorMessage']
            if ($null -ne $ldapErrorProperty -and
                -not [string]::IsNullOrWhiteSpace([string]$ldapErrorProperty.Value)) {
                $ldapDiagnostic = [string]$ldapErrorProperty.Value
            }
            $currentException = $currentException.InnerException
        }
        if (($exceptionMessages -join ' ') -match
            'certificate was rejected|certificate validation|authentication failed') {
            if ($null -eq $script:TrustedCertificate) {
                throw "The LDAP server certificate for '$Server' is not trusted. Supply the issuing CA certificate, or the directly self-signed server certificate for a lab, through -ActiveDirectoryTrustedCertificatePath."
            }
            throw "The LDAP server certificate for '$Server' did not match the supplied trust anchor '$($script:TrustedCertificate.Thumbprint)' or failed DNS-name, validity, EKU, chain, or revocation validation."
        }
        if ($ldapResultCode -eq 49) {
            $diagnosticCode = if ($ldapDiagnostic -match '(?i)\bdata\s+([0-9a-f]+)\b') {
                $Matches[1].ToLowerInvariant()
            }
            else {
                ''
            }
            $credentialFailure = switch ($diagnosticCode) {
                '525' { 'The Active Directory service account was not found.' }
                '52e' { 'The Active Directory service-account password is invalid.' }
                '530' { 'The Active Directory service account may not sign in at this time.' }
                '531' { 'The Active Directory service account may not sign in from this computer.' }
                '532' { 'The Active Directory service-account password has expired.' }
                '533' { 'The Active Directory service account is disabled.' }
                '701' { 'The Active Directory service account has expired.' }
                '773' { 'The Active Directory service account must change its password before it can bind.' }
                '775' { 'The Active Directory service account is locked.' }
                default { 'Active Directory rejected the service-account credential.' }
            }
            throw "$credentialFailure Verify or reset the account state and recreate ADServiceAccountCredential."
        }
        throw
    }
}

function Import-InstallerLdapClient {
    param([Parameter(Mandatory)][string]$PackagePath)

    if ('Novell.Directory.Ldap.LdapConnection' -as [type]) {
        return
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        foreach ($assemblyName in @(
            'Microsoft.Extensions.Logging.Abstractions.dll',
            'Novell.Directory.Ldap.NETStandard.dll'
        )) {
            $entry = $archive.Entries |
                Where-Object { $_.Name -eq $assemblyName } |
                Select-Object -First 1
            if ($null -eq $entry) {
                throw "Function package '$PackagePath' does not contain $assemblyName."
            }
            $stream = $entry.Open()
            try {
                $memory = [System.IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($memory)
                    $memory.Position = 0
                    $null = [System.Runtime.Loader.AssemblyLoadContext]::Default.
                        LoadFromStream($memory)
                }
                finally {
                    $memory.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-LdapSearch {
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter()][AllowEmptyString()][string]$BaseDn = '',
        [Parameter(Mandatory)][string]$Filter,
        [Parameter(Mandatory)][int]$Scope,
        [Parameter(Mandatory)][string[]]$Attributes
    )

    $constraints = [Novell.Directory.Ldap.LdapSearchConstraints]::new()
    $constraints.SetControls(
        [Novell.Directory.Ldap.LdapControl]::new(
            '1.2.840.113556.1.4.1339',
            $false,
            $null))
    $results = $Connection.SearchAsync(
        $BaseDn,
        $Scope,
        $Filter,
        $Attributes,
        $false,
        $constraints,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $entries = [Collections.Generic.List[object]]::new()
    try {
        while ($results.HasMoreAsync(
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()) {
            $entries.Add($results.NextAsync(
                [Threading.CancellationToken]::None).GetAwaiter().GetResult())
        }
    }
    catch {
        $isReferral = $false
        $currentException = $_.Exception
        while ($null -ne $currentException) {
            $resultCodeProperty = $currentException.PSObject.Properties['ResultCode']
            if ($null -ne $resultCodeProperty -and
                [int]$resultCodeProperty.Value -eq 10) {
                $isReferral = $true
                break
            }
            $currentException = $currentException.InnerException
        }
        if (-not $isReferral) {
            throw
        }
        Write-Verbose "Ignored LDAP referrals outside the explicitly queried naming context '$BaseDn'."
    }
    @($entries)
}

function Get-LdapAttributeValue {
    param(
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)][string]$Name
    )

    $attribute = $Entry.GetAttributeSet().GetAttribute($Name)
    if ($null -eq $attribute) {
        return $null
    }
    $attribute.StringValue
}

function Confirm-LdapFallbackUse {
    Write-Warning "LDAPS is unavailable. Continuing securely with LDAP StartTLS on port $LdapFallbackPort."
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

        $null = Confirm-LdapFallbackUse
        $connection = Connect-LdapDirectory `
            -Server $Server `
            -Port $LdapFallbackPort `
            -UseSsl $false `
            -UseStartTls $true
        $script:EffectiveAllowLdapFallback = $true
        $connection
    }
}

function Connect-NextAvailableLdapDirectory {
    param(
        [Parameter(Mandatory)][string[]]$Servers,
        [Parameter(Mandatory)][string]$Domain
    )

    $serverList = @($Servers | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($serverList.Count -eq 0) {
        throw "No Active Directory domain controllers are available for '$Domain'."
    }

    for ($index = 0; $index -lt $serverList.Count; $index++) {
        $server = $serverList[$index]
        try {
            $connection = Connect-ValidatedLdapDirectory -Server $server
            $script:LastConnectedActiveDirectoryServer = $server
            return $connection
        }
        catch {
            $hasNextServer = $index -lt ($serverList.Count - 1)
            if (-not $hasNextServer -or
                -not (Test-LdapConnectionUnavailable -Exception $_.Exception)) {
                throw
            }
            Write-Warning "Domain controller '$server' is unavailable; trying '$($serverList[$index + 1])'."
        }
    }
}

function Get-ActiveDirectoryDomainControllerList {
    param([Parameter(Mandatory)][string]$Domain)

    $queryName = "_ldap._tcp.dc._msdcs.$($Domain.Trim().TrimEnd('.'))"
    try {
        $records = @(Resolve-DnsName `
            -Name $queryName `
            -Type SRV `
            -DnsOnly `
            -ErrorAction Stop | Where-Object {
                $null -ne $_.PSObject.Properties['NameTarget'] -and
                -not [string]::IsNullOrWhiteSpace([string]$_.NameTarget)
            })
    }
    catch {
        throw "Active Directory DNS discovery failed for '$Domain' using '$queryName': $($_.Exception.Message)"
    }
    if ($records.Count -eq 0) {
        throw "Active Directory DNS discovery returned no domain controllers for '$Domain' using '$queryName'."
    }

    @($records |
        Sort-Object Priority, @{ Expression = 'Weight'; Descending = $true } |
        ForEach-Object { ([string]$_.NameTarget).TrimEnd('.') } |
        Select-Object -Unique)
}

function Test-LdapConnectionUnavailable {
    param([Parameter(Mandatory)][Exception]$Exception)

    $currentException = $Exception
    while ($null -ne $currentException) {
        $resultCodeProperty = $currentException.PSObject.Properties['ResultCode']
        if ($null -ne $resultCodeProperty -and
            [int]$resultCodeProperty.Value -in 51, 52, 81, 85, 91) {
            return $true
        }
        if ($currentException -is [System.DirectoryServices.Protocols.LdapException]) {
            return $currentException.ErrorCode -in 51, 52, 81, 85, 91
        }
        $currentException = $currentException.InnerException
    }

    $false
}

function Assert-LdapStartTlsEndpoint {
    param(
        [Parameter(Mandatory)][string]$Server,
        [Parameter(Mandatory)][int]$Port
    )

    $options = [Novell.Directory.Ldap.LdapConnectionOptions]::new().
        ConfigureRemoteCertificateValidationCallback(
            [HybridDeviceTaggerLdapCertificateValidatorV4]::CreateRemote(
                $Server,
                $script:TrustedCertificate))
    $connection = [Novell.Directory.Ldap.LdapConnection]::new($options)
    $connection.ConnectionTimeout = 15000
    try {
        $null = $connection.ConnectAsync(
            $Server,
            $Port,
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $null = $connection.StartTlsAsync(
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }
    finally {
        $connection.Dispose()
    }
}

function Test-LdapConfiguration {
    if ($ADServiceAccountCredential.UserName -ine $ADServiceAccount) {
        throw "ADServiceAccountCredential.UserName must match ADServiceAccount."
    }

    $connection = $null
    $script:EffectiveAllowLdapFallback = $false
    try {
        $connection = Connect-NextAvailableLdapDirectory `
            -Servers $script:ActiveDirectoryServerCandidates `
            -Domain $ActiveDirectoryForestDomain
        $rootEntries = @(Invoke-LdapSearch `
            -Connection $connection `
            -BaseDn '' `
            -Filter '(objectClass=*)' `
            -Scope ([Novell.Directory.Ldap.LdapConnection]::ScopeBase) `
            -Attributes @('configurationNamingContext', 'defaultNamingContext', 'rootDomainNamingContext', 'dnsHostName'))
        if ($rootEntries.Count -ne 1) {
            throw 'The LDAP RootDSE query did not return exactly one entry.'
        }

        $expectedForestDn = Convert-DnsNameToDistinguishedName $ActiveDirectoryForestDomain
        $rootDomainNamingContext = Get-LdapAttributeValue `
            -Entry $rootEntries[0] `
            -Name 'rootDomainNamingContext'
        if ($rootDomainNamingContext -ine $expectedForestDn) {
            throw "AD forest mismatch. Expected '$expectedForestDn', received '$rootDomainNamingContext'."
        }

        $configurationNamingContext = Get-LdapAttributeValue `
            -Entry $rootEntries[0] `
            -Name 'configurationNamingContext'
        $partitionEntries = @(Invoke-LdapSearch `
            -Connection $connection `
            -BaseDn "CN=Partitions,$configurationNamingContext" `
            -Filter '(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3))' `
            -Scope ([Novell.Directory.Ldap.LdapConnection]::ScopeOne) `
            -Attributes @('nCName', 'dnsRoot'))
        $forestDomains = @($partitionEntries | ForEach-Object {
            [pscustomobject]@{
                NamingContext = Get-LdapAttributeValue -Entry $_ -Name 'nCName'
                DnsRoot = Get-LdapAttributeValue -Entry $_ -Name 'dnsRoot'
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

    $accountDomain = Get-DomainDnsNameFromDistinguishedName $ActiveDirectoryBindUserDistinguishedName
    $accountServers = if ($accountDomain -ieq $ActiveDirectoryForestDomain) {
        $script:ActiveDirectoryServerCandidates
    }
    else {
        Get-ActiveDirectoryDomainControllerList -Domain $accountDomain
    }
    $accountConnection = $null
    try {
        $accountConnection = Connect-NextAvailableLdapDirectory `
            -Servers $accountServers `
            -Domain $accountDomain
        $accountEntries = @(Invoke-LdapSearch `
            -Connection $accountConnection `
            -BaseDn $ActiveDirectoryBindUserDistinguishedName `
            -Filter '(objectClass=user)' `
            -Scope ([Novell.Directory.Ldap.LdapConnection]::ScopeBase) `
            -Attributes @('distinguishedName', 'userAccountControl', 'pwdLastSet'))
        if ($accountEntries.Count -ne 1) {
            throw "AD service account '$ActiveDirectoryBindUserDistinguishedName' was not found."
        }
    }
    finally {
        if ($null -ne $accountConnection) {
            $accountConnection.Dispose()
        }
    }

    $transport = if ($script:EffectiveAllowLdapFallback) { 'LDAP with StartTLS' } else { 'LDAPS' }
    Write-Host "Secure Active Directory validation succeeded through $transport." -ForegroundColor Green
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
    param([Parameter(Mandatory)]$Vault)

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

}

function Initialize-CurrentPasswordThroughFunction {
    $tokenResult = Invoke-AzureCli -Arguments @(
        'account', 'get-access-token', '--subscription', $SubscriptionId,
        '--scope', "api://$ManagementApiClientId/.default", '--only-show-errors', '--output', 'json'
    )
    $accessToken = ($tokenResult.Output | ConvertFrom-Json).accessToken
    $plainPassword = $ADServiceAccountCredential.GetNetworkCredential().Password
    try {
        $body = @{ password = $plainPassword } | ConvertTo-Json -Compress
        $uri = "https://$FunctionAppName.azurewebsites.net/api/management/actions/initialize-ad-credential"
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            try {
                $null = Invoke-RestMethod `
                    -Method Post `
                    -Uri $uri `
                    -Headers @{ Authorization = "Bearer $accessToken" } `
                    -ContentType 'application/json' `
                    -Body $body
                break
            }
            catch {
                $responseProperty = $_.Exception.PSObject.Properties['Response']
                $statusCode = if ($null -ne $responseProperty -and
                    $null -ne $responseProperty.Value) {
                    $statusCodeProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
                    if ($null -ne $statusCodeProperty) {
                        [int]$statusCodeProperty.Value
                    }
                    else {
                        0
                    }
                }
                else {
                    0
                }
                if ($statusCode -eq 409) {
                    Write-Host 'The Active Directory credential is already initialized.' -ForegroundColor Green
                    return
                }
                if ($statusCode -notin @(404, 502, 503, 504) -or $attempt -eq 6) {
                    $errorDetail = ''
                    $errorDetailsProperty = $_.PSObject.Properties['ErrorDetails']
                    if ($null -ne $errorDetailsProperty -and
                        $null -ne $errorDetailsProperty.Value) {
                        $messageProperty = $errorDetailsProperty.Value.PSObject.Properties['Message']
                        $errorDetail = if ($null -ne $messageProperty) {
                            [string]$messageProperty.Value
                        }
                        else {
                            [string]$errorDetailsProperty.Value
                        }
                    }
                    if ([string]::IsNullOrWhiteSpace($errorDetail)) {
                        $errorDetail = [string]$_.Exception.Message
                    }
                    throw "Active Directory credential initialization failed with HTTP $statusCode`: $errorDetail"
                }
                Write-Warning "Function API is not ready; retrying credential initialization ($attempt/6)."
                Start-Sleep -Seconds 10
            }
        }
    }
    finally {
        $plainPassword = $null
        $body = $null
        $accessToken = $null
    }

    Write-Host 'The Function validated and initialized the Active Directory credential.' -ForegroundColor Green
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
        createKeyVault = [bool]$CreateKeyVault
        keyVaultResourceGroupName = $KeyVaultResourceGroupName
        keyVaultSubscriptionId = $KeyVaultSubscriptionId
        keyVaultPrivateDnsZoneResourceId = $script:KeyVaultPrivateDnsZoneResourceId
        storagePrivateDnsZoneResourceId = $script:StoragePrivateDnsZoneResourceId
        activeDirectoryDomain = $ActiveDirectoryForestDomain
        allowLdapFallback = $true
        ldapFallbackPort = $LdapFallbackPort
        activeDirectoryTrustedCertificatePath = if ($ActiveDirectoryTrustedCertificatePath) {
            $trustedCertificatePackagePath
        } else {
            ''
        }
        activeDirectoryBindUsername = $ADServiceAccount
        activeDirectoryBindUserDistinguishedName = $ActiveDirectoryBindUserDistinguishedName
        managementApiClientId = $ManagementApiClientId
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

function Get-ManagementApiRoleDefinition {
    @(
        [ordered]@{
            id = 'bbd6f683-ae76-427f-9600-fef96529b9f5'
            value = 'HybridDeviceTagger.Reader'
            displayName = 'HybridDeviceTagger Reader'
            description = 'Read tagging rules, operation statuses, and device-tagging traces.'
            allowedMemberTypes = @('User', 'Application')
            isEnabled = $true
        },
        [ordered]@{
            id = '3410aec6-3808-4a5d-ae7c-408d1dc5f9ca'
            value = 'HybridDeviceTagger.RuleAdministrator'
            displayName = 'HybridDeviceTagger Rule Administrator'
            description = 'Read, add, replace, and remove tagging rules.'
            allowedMemberTypes = @('User', 'Application')
            isEnabled = $true
        },
        [ordered]@{
            id = '73647ecc-d6ab-4db9-868c-69ec04e5f70f'
            value = 'HybridDeviceTagger.TaggingOperator'
            displayName = 'HybridDeviceTagger Tagging Operator'
            description = 'Read tagging diagnostics and start immediate device-tagging runs.'
            allowedMemberTypes = @('User', 'Application')
            isEnabled = $true
        },
        [ordered]@{
            id = 'f3dd0b4b-4168-497e-82c6-56be89c05c3b'
            value = 'HybridDeviceTagger.PasswordRotationAdministrator'
            displayName = 'HybridDeviceTagger Password Rotation Administrator'
            description = 'Read password-rotation status and force service-account password rotation.'
            allowedMemberTypes = @('User', 'Application')
            isEnabled = $true
        },
        [ordered]@{
            id = '659858ff-b645-4b06-90ff-e79314628687'
            value = 'HybridDeviceTagger.CertificateAdministrator'
            displayName = 'HybridDeviceTagger Certificate Administrator'
            description = 'Read and change Active Directory LDAPS trust certificates.'
            allowedMemberTypes = @('User', 'Application')
            isEnabled = $true
        }
    )
}

function New-ManagementApiApplication {
    $roleDefinitions = @(Get-ManagementApiRoleDefinition)
    $body = ConvertTo-AzureCliJsonArgument -Json (@{
        displayName = "$FunctionAppName Management API"
        signInAudience = 'AzureADMyOrg'
        appRoles = $roleDefinitions
    } | ConvertTo-Json -Depth 10 -Compress)
    $application = Get-AzureJson -Arguments @(
        'rest', '--method', 'post',
        '--url', 'https://graph.microsoft.com/v1.0/applications',
        '--headers', 'Content-Type=application/json',
        '--body', $body
    )
    if ([string]::IsNullOrWhiteSpace([string]$application.id) -or
        [string]::IsNullOrWhiteSpace([string]$application.appId)) {
        throw 'Microsoft Graph did not return the created management API application identifiers.'
    }

    $script:CreatedManagementApiApplicationObjectId = [string]$application.id
    $ManagementApiClientId = [string]$application.appId
    Set-Variable -Name ManagementApiClientId -Scope Script -Value $ManagementApiClientId

    $identifierBody = ConvertTo-AzureCliJsonArgument -Json (@{
        identifierUris = @("api://$ManagementApiClientId")
    } | ConvertTo-Json -Compress)
    $null = Invoke-AzureCli -Arguments @(
        'rest', '--method', 'patch',
        '--url', "https://graph.microsoft.com/v1.0/applications/$($application.id)",
        '--headers', 'Content-Type=application/json',
        '--body', $identifierBody,
        '--only-show-errors'
    )

    $servicePrincipalBody = ConvertTo-AzureCliJsonArgument -Json (@{
        appId = $ManagementApiClientId
    } | ConvertTo-Json -Compress)
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $servicePrincipalResult = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'post',
            '--url', 'https://graph.microsoft.com/v1.0/servicePrincipals',
            '--headers', 'Content-Type=application/json',
            '--body', $servicePrincipalBody,
            '--only-show-errors', '--output', 'json'
        ) -AllowFailure
        if ($servicePrincipalResult.ExitCode -eq 0) {
            Write-Host "Created management API Entra application '$ManagementApiClientId'." -ForegroundColor Green
            return
        }
        if ($attempt -lt 6) {
            Start-Sleep -Seconds 5
        }
    }

    throw "The management API application was created, but its Enterprise Application could not be created:`n$($servicePrincipalResult.Output)"
}

function Set-ManagementApiAuthorization {
    $azureCliApplicationId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
    $delegatedPermissionId = '7d5db24c-bf9d-4b19-9b31-4f021da659be'
    $applicationResponse = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/applications?`$filter=appId%20eq%20%27$ManagementApiClientId%27&`$select=id,appId,appRoles,identifierUris,api"
    )
    $application = @($applicationResponse.value)[0]
    if (-not $application) {
        throw "Management API application '$ManagementApiClientId' was not found in Microsoft Entra ID."
    }

    $servicePrincipalResponse = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId%20eq%20%27$ManagementApiClientId%27&`$select=id,appId,appRoleAssignmentRequired"
    )
    $servicePrincipal = @($servicePrincipalResponse.value)[0]
    if (-not $servicePrincipal) {
        throw "Management API enterprise application '$ManagementApiClientId' was not found."
    }

    $roleDefinitions = @(Get-ManagementApiRoleDefinition)
    $requiredIdentifierUri = "api://$ManagementApiClientId"
    if (@($application.identifierUris) -notcontains $requiredIdentifierUri) {
        $identifierBody = ConvertTo-AzureCliJsonArgument -Json (@{
            identifierUris = @(@($application.identifierUris) + $requiredIdentifierUri)
        } | ConvertTo-Json -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'patch',
            '--url', "https://graph.microsoft.com/v1.0/applications/$($application.id)",
            '--headers', 'Content-Type=application/json',
            '--body', $identifierBody,
            '--only-show-errors'
        )
        Write-Host "Management API identifier URI '$requiredIdentifierUri' configured."
    }

    $delegatedPermission = [ordered]@{
        adminConsentDescription = 'Access HybridDeviceTagger management operations as the signed-in user.'
        adminConsentDisplayName = 'Access HybridDeviceTagger management operations'
        id = $delegatedPermissionId
        isEnabled = $true
        type = 'User'
        userConsentDescription = 'Access HybridDeviceTagger management operations that you are authorized to perform.'
        userConsentDisplayName = 'Access HybridDeviceTagger management operations'
        value = 'access_as_user'
    }
    $updatedScopes = [System.Collections.Generic.List[object]]::new()
    foreach ($existingScope in @($application.api.oauth2PermissionScopes)) {
        if ([string]$existingScope.id -eq $delegatedPermissionId -or
            [string]$existingScope.value -eq $delegatedPermission.value) {
            continue
        }
        $updatedScopes.Add($existingScope)
    }
    $updatedScopes.Add($delegatedPermission)
    $currentDelegatedPermission = @($application.api.oauth2PermissionScopes | Where-Object {
        [string]$_.id -eq $delegatedPermissionId -or
        [string]$_.value -eq $delegatedPermission.value
    })[0]
    $scopeNeedsUpdate = $null -eq $currentDelegatedPermission -or
        [string]$currentDelegatedPermission.id -ne $delegatedPermissionId -or
        [string]$currentDelegatedPermission.value -cne $delegatedPermission.value -or
        [string]$currentDelegatedPermission.type -cne $delegatedPermission.type -or
        [bool]$currentDelegatedPermission.isEnabled -ne $delegatedPermission.isEnabled -or
        [string]$currentDelegatedPermission.adminConsentDescription -cne
            $delegatedPermission.adminConsentDescription -or
        [string]$currentDelegatedPermission.adminConsentDisplayName -cne
            $delegatedPermission.adminConsentDisplayName -or
        [string]$currentDelegatedPermission.userConsentDescription -cne
            $delegatedPermission.userConsentDescription -or
        [string]$currentDelegatedPermission.userConsentDisplayName -cne
            $delegatedPermission.userConsentDisplayName

    $updatedPreAuthorizedApplications = [System.Collections.Generic.List[object]]::new()
    foreach ($existingPreAuthorization in @($application.api.preAuthorizedApplications)) {
        if ([string]$existingPreAuthorization.appId -eq $azureCliApplicationId) {
            continue
        }
        $updatedPreAuthorizedApplications.Add($existingPreAuthorization)
    }
    $updatedPreAuthorizedApplications.Add([ordered]@{
        appId = $azureCliApplicationId
        delegatedPermissionIds = @($delegatedPermissionId)
    })
    $currentPreAuthorization = @($application.api.preAuthorizedApplications | Where-Object {
        [string]$_.appId -eq $azureCliApplicationId
    })[0]
    $preAuthorizationNeedsUpdate = $null -eq $currentPreAuthorization -or
        @($currentPreAuthorization.delegatedPermissionIds) -notcontains $delegatedPermissionId

    if ($scopeNeedsUpdate) {
        $scopeBody = ConvertTo-AzureCliJsonArgument -Json (@{
            api = @{
                oauth2PermissionScopes = @($updatedScopes)
            }
        } | ConvertTo-Json -Depth 10 -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'patch',
            '--url', "https://graph.microsoft.com/v1.0/applications/$($application.id)",
            '--headers', 'Content-Type=application/json',
            '--body', $scopeBody,
            '--only-show-errors'
        )
        Write-Host 'Management API delegated scope configured.'
    }

    if ($preAuthorizationNeedsUpdate) {
        $preAuthorizationBody = ConvertTo-AzureCliJsonArgument -Json (@{
            api = @{
                preAuthorizedApplications = @($updatedPreAuthorizedApplications)
            }
        } | ConvertTo-Json -Depth 10 -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'patch',
            '--url', "https://graph.microsoft.com/v1.0/applications/$($application.id)",
            '--headers', 'Content-Type=application/json',
            '--body', $preAuthorizationBody,
            '--only-show-errors'
        )
        Write-Host 'Management API delegated access configured for Microsoft Azure CLI.'
    }
    if (-not $scopeNeedsUpdate -and -not $preAuthorizationNeedsUpdate) {
        Write-Host 'Management API delegated access for Microsoft Azure CLI is already configured.'
    }

    $updatedRoles = [System.Collections.Generic.List[object]]::new()
    foreach ($existingRole in @($application.appRoles)) {
        if ($roleDefinitions.id -notcontains [string]$existingRole.id) {
            $conflict = @($roleDefinitions | Where-Object value -EQ ([string]$existingRole.value))[0]
            if ($conflict) {
                throw "Application role '$($existingRole.value)' already exists with unexpected ID '$($existingRole.id)'."
            }
            $updatedRoles.Add($existingRole)
        }
    }
    foreach ($roleDefinition in $roleDefinitions) {
        $updatedRoles.Add($roleDefinition)
    }

    $rolesNeedUpdate = $false
    foreach ($roleDefinition in $roleDefinitions) {
        $currentRole = @($application.appRoles | Where-Object id -EQ $roleDefinition.id)[0]
        $memberTypesDiffer = $null -eq $currentRole -or
            $null -ne (Compare-Object `
                @($currentRole.allowedMemberTypes) `
                @($roleDefinition.allowedMemberTypes))
        if (-not $currentRole -or
            [string]$currentRole.value -cne $roleDefinition.value -or
            [string]$currentRole.displayName -cne $roleDefinition.displayName -or
            [string]$currentRole.description -cne $roleDefinition.description -or
            [bool]$currentRole.isEnabled -ne $roleDefinition.isEnabled -or
            $memberTypesDiffer) {
            $rolesNeedUpdate = $true
            break
        }
    }
    if ($rolesNeedUpdate) {
        $body = ConvertTo-AzureCliJsonArgument -Json (@{
            appRoles = @($updatedRoles)
        } | ConvertTo-Json -Depth 10 -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'patch',
            '--url', "https://graph.microsoft.com/v1.0/applications/$($application.id)",
            '--headers', 'Content-Type=application/json',
            '--body', $body,
            '--only-show-errors'
        )
        Write-Host 'Management API application roles configured.'
    }

    $existingAssignments = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$($servicePrincipal.id)/appRoleAssignedTo?`$select=principalId,appRoleId"
    )
    foreach ($roleDefinition in $roleDefinitions) {
        foreach ($principalId in @($script:ManagementApiRoleAssignments[$roleDefinition.value])) {
            $alreadyAssigned = @($existingAssignments.value | Where-Object {
                [string]$_.principalId -eq [string]$principalId -and
                [string]$_.appRoleId -eq [string]$roleDefinition.id
            }).Count -gt 0
            if ($alreadyAssigned) {
                continue
            }

            $body = ConvertTo-AzureCliJsonArgument -Json (@{
                principalId = [string]$principalId
                resourceId = [string]$servicePrincipal.id
                appRoleId = [string]$roleDefinition.id
            } | ConvertTo-Json -Compress)
            $null = Invoke-AzureCli -Arguments @(
                'rest', '--method', 'post',
                '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$($servicePrincipal.id)/appRoleAssignedTo",
                '--headers', 'Content-Type=application/json',
                '--body', $body,
                '--only-show-errors'
            )
            Write-Host "Assigned $($roleDefinition.value) to principal '$principalId'."
        }
    }

    if ($servicePrincipal.appRoleAssignmentRequired) {
        $body = ConvertTo-AzureCliJsonArgument -Json (@{
            appRoleAssignmentRequired = $false
        } | ConvertTo-Json -Compress)
        $null = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'patch',
            '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$($servicePrincipal.id)",
            '--headers', 'Content-Type=application/json',
            '--body', $body,
            '--only-show-errors'
        )
        Write-Host 'Management API enterprise application allows Function App RBAC authorization.'
    }
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
        $null = New-Item -ItemType Directory -Path $publishPath -Force -Confirm:$false
        [System.IO.Compression.ZipFile]::ExtractToDirectory(
            $FunctionPackagePath,
            $publishPath)
        foreach ($requiredPackageFile in @('host.json', 'worker.config.json', 'functions.metadata')) {
            if (-not (Test-Path (Join-Path $publishPath $requiredPackageFile) -PathType Leaf)) {
                throw "Function package is missing required file '$requiredPackageFile'."
            }
        }
        if ($ActiveDirectoryTrustedCertificatePath) {
            $certificateDirectory = Join-Path $publishPath 'certificates'
            $null = New-Item `
                -ItemType Directory `
                -Path $certificateDirectory `
                -Force `
                -Confirm:$false
            [System.IO.File]::WriteAllText(
                (Join-Path $publishPath $trustedCertificatePackagePath),
                $script:TrustedCertificate.ExportCertificatePem(),
                [System.Text.Encoding]::ASCII)
        }
        Compress-Archive `
            -Path (Join-Path $publishPath '*') `
            -DestinationPath $zipPath `
            -CompressionLevel Optimal `
            -Confirm:$false
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
            Remove-Item -Path $temporaryRoot -Recurse -Force -Confirm:$false
        }
    }
}

function Get-PrivateEndpointAddress {
    param(
        [Parameter(Mandatory)][string]$PrivateEndpointName,
        [Parameter(Mandatory)][string]$PrivateEndpointLabel
    )

    $privateEndpoint = Get-AzureJson -Arguments @(
        'network', 'private-endpoint', 'show',
        '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName,
        '--name', $PrivateEndpointName
    )
    $networkInterfaceId = [string](@($privateEndpoint.networkInterfaces)[0].id)
    if ([string]::IsNullOrWhiteSpace($networkInterfaceId)) {
        throw "$PrivateEndpointLabel private endpoint '$PrivateEndpointName' does not expose a network interface."
    }
    $networkInterface = Get-AzureJson -Arguments @(
        'network', 'nic', 'show',
        '--subscription', $SubscriptionId,
        '--ids', $networkInterfaceId
    )
    $expectedAddresses = @(
        $networkInterface.ipConfigurations |
            ForEach-Object { [string]$_.privateIPAddress } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($expectedAddresses.Count -eq 0) {
        throw "$PrivateEndpointLabel private endpoint '$PrivateEndpointName' does not have a private IP address."
    }

    $expectedAddresses
}

function Assert-CustomDnsPrivateEndpointResolution {
    param(
        [Parameter(Mandatory)][string[]]$DnsServers,
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][string]$PrivateDnsZoneName,
        [Parameter(Mandatory)][string[]]$ExpectedAddresses,
        [Parameter(Mandatory)][string]$EndpointLabel
    )

    foreach ($dnsServer in $DnsServers) {
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            $resolvedAddresses = @(
                Resolve-DnsName `
                    -Name $HostName `
                    -Server $dnsServer `
                    -Type A `
                    -DnsOnly `
                    -ErrorAction SilentlyContinue |
                    Where-Object { $null -ne $_.PSObject.Properties['IPAddress'] } |
                    ForEach-Object { [string]$_.PSObject.Properties['IPAddress'].Value } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            )
            if (@($resolvedAddresses | Where-Object { $ExpectedAddresses -contains $_ }).Count -gt 0) {
                break
            }
            if ($attempt -lt 6) {
                Start-Sleep -Seconds 5
            }
        }

        if (@($resolvedAddresses | Where-Object { $ExpectedAddresses -contains $_ }).Count -eq 0) {
            throw @"
Custom DNS server '$dnsServer' does not resolve '$HostName' to the $EndpointLabel private endpoint address $($ExpectedAddresses -join ', ').
Configure a conditional forwarder for '$PrivateDnsZoneName' to Azure DNS '168.63.129.16', then retry the installation.
On a Windows DNS server, run as a DNS administrator:
Add-DnsServerConditionalForwarderZone -Name '$PrivateDnsZoneName' -MasterServers 168.63.129.16 -ReplicationScope Forest
"@
        }
    }
}

function Assert-PrivateEndpointDnsResolution {
    $customDnsServers = @($virtualNetwork.dhcpOptions.dnsServers)
    if ($customDnsServers.Count -eq 0) {
        return
    }

    $storageAccount = Get-AzureJson -Arguments @(
        'storage', 'account', 'show',
        '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName,
        '--name', $StorageAccountName
    )
    Assert-CustomDnsPrivateEndpointResolution `
        -DnsServers $customDnsServers `
        -HostName ([uri][string]$storageAccount.primaryEndpoints.blob).Host `
        -PrivateDnsZoneName 'privatelink.blob.core.windows.net' `
        -ExpectedAddresses @(Get-PrivateEndpointAddress `
            -PrivateEndpointName "pe-$StorageAccountName-blob" `
            -PrivateEndpointLabel 'Storage') `
        -EndpointLabel 'Storage'

    if ($CreateKeyVault) {
        $vault = Get-AzureJson -Arguments @(
            'keyvault', 'show',
            '--subscription', $KeyVaultSubscriptionId,
            '--resource-group', $KeyVaultResourceGroupName,
            '--name', $KeyVaultName
        )
        Assert-CustomDnsPrivateEndpointResolution `
            -DnsServers $customDnsServers `
            -HostName ([uri][string]$vault.properties.vaultUri).Host `
            -PrivateDnsZoneName 'privatelink.vaultcore.azure.net' `
            -ExpectedAddresses @(Get-PrivateEndpointAddress `
                -PrivateEndpointName "pe-$KeyVaultName" `
                -PrivateEndpointLabel 'Key Vault') `
            -EndpointLabel 'Key Vault'
    }

    Write-Host "Private endpoint DNS resolution validated through custom DNS server(s): $($customDnsServers -join ', ')." -ForegroundColor Green
}

$installerLogPath = Join-Path ([System.IO.Path]::GetTempPath()) 'HybridDeviceTagger-Install.log'
$transcriptStarted = $false
$compactFailure = $false
try {
    Start-Transcript -Path $installerLogPath -Force -Confirm:$false | Out-Null
    $transcriptStarted = $true
    Write-Host "Installer log: $installerLogPath"

Write-Step 'Validating Azure subscription'
Assert-Command -Name az -InstallHint 'Install Azure CLI 2.60.0 or newer.'
$account = Get-AzureJson -Arguments @('account', 'show')
if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
    if ($script:NonInteractive) {
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
    if ($script:NonInteractive) {
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
    if ($script:NonInteractive) {
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
    if ($script:NonInteractive) {
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
    if ($script:NonInteractive) {
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
        -not $script:NonInteractive) {
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
        $script:ExpectedVirtualNetworkId = [string]$virtualNetwork.id
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
$script:StoragePrivateDnsZoneResourceId = Get-PrivateDnsZoneResourceId `
    -ZoneName 'privatelink.blob.core.windows.net'
if ($script:StoragePrivateDnsZoneResourceId) {
    Write-Host "Reusing Storage private DNS zone '$($script:StoragePrivateDnsZoneResourceId)'."
}

if ([string]::IsNullOrWhiteSpace($ActiveDirectoryForestDomain)) {
    $defaultActiveDirectoryForestDomain = Get-LoggedOnUserForestDomain
    if ([string]::IsNullOrWhiteSpace($defaultActiveDirectoryForestDomain)) {
        $ActiveDirectoryForestDomain = Read-RequiredInstallerValue `
            -Name 'ActiveDirectoryForestDomain' `
            -Prompt 'Active Directory forest DNS name'
    }
    elseif (-not $script:NonInteractive) {
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
    else {
        $ActiveDirectoryForestDomain = $defaultActiveDirectoryForestDomain
    }
}

if ([string]::IsNullOrWhiteSpace($ActiveDirectoryServer)) {
    $script:ActiveDirectoryServerCandidates = @(
        Get-ActiveDirectoryDomainControllerList -Domain $ActiveDirectoryForestDomain
    )
    $ActiveDirectoryServer = $script:ActiveDirectoryServerCandidates[0]
    Write-Host (
        "Discovered Active Directory domain controllers through DNS SRV: {0}" -f
        ($script:ActiveDirectoryServerCandidates -join ', ')
    )
}
else {
    $script:ActiveDirectoryServerCandidates = @($ActiveDirectoryServer)
}

if ([string]::IsNullOrWhiteSpace($ADServiceAccount)) {
    if ($null -ne $ADServiceAccountCredential) {
        $ADServiceAccount = $ADServiceAccountCredential.UserName.Trim()
    }
    elseif ($script:NonInteractive) {
        throw 'ADServiceAccountCredential or ADServiceAccount must be supplied explicitly for noninteractive execution.'
    }
    else {
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
}
if ($ADServiceAccount -notmatch '^[^@\s]+@[^@\s]+$') {
    throw 'ADServiceAccount must be a user principal name such as service-account@contoso.com.'
}

if (-not $SkipActiveDirectoryDirectoryChecks -and
    $null -eq $ADServiceAccountCredential) {
    if ($script:NonInteractive) {
        throw 'ADServiceAccountCredential must be supplied explicitly for noninteractive execution.'
    }
    $ADServiceAccountCredential = Get-Credential `
        -UserName $ADServiceAccount `
        -Message 'Enter the Active Directory service-account password for bind validation'
    if ($null -eq $ADServiceAccountCredential) {
        throw 'Active Directory service-account credential entry was canceled.'
    }
}
if ($null -ne $ADServiceAccountCredential -and
    $ADServiceAccountCredential.UserName -ine $ADServiceAccount) {
    throw 'The credential user name must match the selected AD service-account UPN.'
}

if (-not $createKeyVaultWasSupplied -and
    -not $script:NonInteractive) {
    $keyVaultChoices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Existing',
            'Use and validate an existing Key Vault.'),
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&New',
            'Create a private Key Vault in the Function App resource group.')
    )
    $CreateKeyVault = $Host.UI.PromptForChoice(
        'Key Vault',
        'Do you want to use an existing Key Vault or create a new one?',
        $keyVaultChoices,
        0) -eq 1
}

if ($CreateKeyVault) {
    if ($SkipActiveDirectoryDirectoryChecks) {
        throw 'Creating a Key Vault requires Active Directory credential validation so the current secret can be initialized.'
    }
    $KeyVaultSubscriptionId = $SubscriptionId
    $KeyVaultResourceGroupName = $ResourceGroupName
    $script:KeyVaultPrivateDnsZoneResourceId = Get-PrivateDnsZoneResourceId `
        -ZoneName 'privatelink.vaultcore.azure.net'
    if ($script:KeyVaultPrivateDnsZoneResourceId) {
        Write-Host "Reusing Key Vault private DNS zone '$($script:KeyVaultPrivateDnsZoneResourceId)'."
    }
}
else {
    $script:KeyVaultPrivateDnsZoneResourceId = ''
    if ([string]::IsNullOrWhiteSpace($KeyVaultSubscriptionId)) {
        $KeyVaultSubscriptionId = if ($script:NonInteractive) {
            $SubscriptionId
        }
        else {
            Read-InstallerValueWithDefault `
                -Prompt 'Existing Key Vault subscription ID' `
                -DefaultValue $SubscriptionId
        }
    }
    if ([string]::IsNullOrWhiteSpace($KeyVaultResourceGroupName)) {
        $KeyVaultResourceGroupName = if ($script:NonInteractive) {
            throw 'KeyVaultResourceGroupName must be supplied explicitly for noninteractive execution.'
        }
        else {
            Read-InstallerValueWithDefault `
                -Prompt 'Existing Key Vault resource group name' `
                -DefaultValue $ResourceGroupName
        }
    }
}

if ([string]::IsNullOrWhiteSpace($KeyVaultName)) {
    if ($script:NonInteractive) {
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

$targetFunctionResult = Invoke-AzureCli -Arguments @(
    'functionapp', 'show',
    '--subscription', $SubscriptionId,
    '--resource-group', $ResourceGroupName,
    '--name', $FunctionAppName,
    '--only-show-errors', '--output', 'none'
) -AllowFailure
$targetFunctionExists = $targetFunctionResult.ExitCode -eq 0
if ([string]::IsNullOrWhiteSpace($ManagementApiClientId)) {
    if (-not $targetFunctionExists) {
        $script:CreateManagementApiApplication = $true
        $ManagementApiClientId = [guid]::NewGuid().ToString()
        Write-Host 'A new management API Entra application will be created automatically after installation approval.' -ForegroundColor Green
    }
    else {
        $appSettingsResult = Invoke-AzureCli -Arguments @(
            'functionapp', 'config', 'appsettings', 'list',
            '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName,
            '--name', $FunctionAppName,
            '--only-show-errors', '--output', 'json'
        ) -AllowFailure
        if ($appSettingsResult.ExitCode -eq 0) {
            $appSettings = @($appSettingsResult.Output | ConvertFrom-Json -Depth 100)
            $managementApiClientIdSetting = $appSettings |
                Where-Object { $_.name -eq 'ManagementApi__ClientId' } |
                Select-Object -First 1
            if ($managementApiClientIdSetting) {
                $ManagementApiClientId = [string]$managementApiClientIdSetting.value
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($ManagementApiClientId)) {
            Write-Host "Reusing management API Entra application '$ManagementApiClientId' from the existing Function App configuration." -ForegroundColor Green
        }
        elseif ($script:NonInteractive) {
            throw "The existing Function App '$FunctionAppName' does not contain the ManagementApi__ClientId application setting. Supply ManagementApiClientId explicitly to resume the installation."
        }
        else {
            $ManagementApiClientId = Read-RequiredInstallerValue `
                -Name 'ManagementApiClientId' `
                -Prompt 'Existing management API Entra application (client) ID'
        }
    }
}
if ($ManagementApiClientId -notmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    throw 'ManagementApiClientId must be a valid GUID.'
}
if ($null -eq $ManagementApiAllowedPrincipalObjectId -or
    @($ManagementApiAllowedPrincipalObjectId).Count -eq 0) {
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
    $signedInUserId = [guid]::Empty
    if ([guid]::TryParse([string]$signedInUser.id, [ref]$signedInUserId)) {
        $ManagementApiAllowedPrincipalObjectId = @($signedInUserId)
        Write-Host "Management API access restricted to signed-in Entra user '$signedInUserId'." -ForegroundColor Green
    }
    elseif ($script:NonInteractive) {
        throw 'ManagementApiAllowedPrincipalObjectId must be supplied explicitly for noninteractive execution.'
    }
    else {
        $allowedPrincipalId = Read-RequiredInstallerValue `
            -Name 'ManagementApiAllowedPrincipalObjectId' `
            -Prompt 'Management API allowed Entra principal object ID'
        $parsedAllowedPrincipalId = [guid]::Empty
        if (-not [guid]::TryParse($allowedPrincipalId, [ref]$parsedAllowedPrincipalId)) {
            throw 'ManagementApiAllowedPrincipalObjectId must be a valid GUID.'
        }
        $ManagementApiAllowedPrincipalObjectId = @($parsedAllowedPrincipalId)
    }
}
$script:ManagementApiRoleAssignments = [ordered]@{
    'HybridDeviceTagger.Reader' = if ($null -ne $ManagementApiReaderPrincipalObjectId) {
        @($ManagementApiReaderPrincipalObjectId)
    } else { $ManagementApiAllowedPrincipalObjectId }
    'HybridDeviceTagger.RuleAdministrator' = if ($null -ne $ManagementApiRuleAdministratorPrincipalObjectId) {
        @($ManagementApiRuleAdministratorPrincipalObjectId)
    } else { $ManagementApiAllowedPrincipalObjectId }
    'HybridDeviceTagger.TaggingOperator' = if ($null -ne $ManagementApiTaggingOperatorPrincipalObjectId) {
        @($ManagementApiTaggingOperatorPrincipalObjectId)
    } else { $ManagementApiAllowedPrincipalObjectId }
    'HybridDeviceTagger.PasswordRotationAdministrator' = if ($null -ne $ManagementApiPasswordRotationAdministratorPrincipalObjectId) {
        @($ManagementApiPasswordRotationAdministratorPrincipalObjectId)
    } else { $ManagementApiAllowedPrincipalObjectId }
    'HybridDeviceTagger.CertificateAdministrator' = if ($null -ne $ManagementApiCertificateAdministratorPrincipalObjectId) {
        @($ManagementApiCertificateAdministratorPrincipalObjectId)
    } else { $ManagementApiAllowedPrincipalObjectId }
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
    @{ Name = 'ADServiceAccount'; Value = $ADServiceAccount; MaximumLength = 256 }
)
foreach ($validation in $requiredInputValidation) {
    if ([string]::IsNullOrWhiteSpace([string]$validation.Value) -or
        ([string]$validation.Value).Length -gt $validation.MaximumLength) {
        throw "$($validation.Name) must contain 1-$($validation.MaximumLength) characters."
    }
}
if ($SkipActiveDirectoryDirectoryChecks -and
    [string]::IsNullOrWhiteSpace($ActiveDirectoryBindUserDistinguishedName)) {
    throw 'ActiveDirectoryBindUserDistinguishedName must be supplied when Active Directory directory checks are skipped.'
}
if (-not [string]::IsNullOrWhiteSpace($ActiveDirectoryBindUserDistinguishedName) -and
    $ActiveDirectoryBindUserDistinguishedName.Length -gt 2048) {
    throw 'ActiveDirectoryBindUserDistinguishedName cannot exceed 2048 characters.'
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
foreach ($tag in $Tags.GetEnumerator()) {
    if ([string]::IsNullOrEmpty([string]$tag.Key) -or ([string]$tag.Key).Length -gt 512) {
        throw 'Azure tag keys must contain 1-512 characters.'
    }
    if (([string]$tag.Value).Length -gt 256) {
        throw "Azure tag '$($tag.Key)' value cannot exceed 256 characters."
    }
}

Write-Step 'Checking local prerequisites'
if (-not (Test-Path $templatePath)) { throw "Bicep template not found: $templatePath" }
if (-not (Test-Path -LiteralPath $FunctionPackagePath -PathType Leaf)) {
    throw "Prebuilt Function package not found: $FunctionPackagePath"
}
Import-InstallerLdapClient -PackagePath $FunctionPackagePath
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
$script:EffectiveAllowLdapFallback = $false

if (-not $SkipActiveDirectoryDirectoryChecks) {
    Write-Step 'Validating existing Active Directory service account through secure LDAP'
    $resolvedServiceAccountDn = Resolve-ADServiceAccountDistinguishedName
    if ([string]::IsNullOrWhiteSpace($resolvedServiceAccountDn)) {
        throw "AD service account '$ADServiceAccount' was not found."
    }
    if (-not [string]::IsNullOrWhiteSpace($ActiveDirectoryBindUserDistinguishedName) -and
        $ActiveDirectoryBindUserDistinguishedName -ine $resolvedServiceAccountDn) {
        throw "ActiveDirectoryBindUserDistinguishedName does not match the account resolved from ADServiceAccount '$ADServiceAccount'."
    }
    $ActiveDirectoryBindUserDistinguishedName = $resolvedServiceAccountDn
    if ($ActiveDirectoryBindUserDistinguishedName.Length -gt 2048) {
        throw 'The resolved Active Directory service-account distinguished name exceeds 2048 characters.'
    }
    Write-Host "Existing Active Directory service account '$ADServiceAccount' validated through secure LDAP." -ForegroundColor Green
}

$azVersion = Get-AzureJson -Arguments @('version')
if ([version]$azVersion.'azure-cli' -lt [version]'2.60.0') {
    throw "Azure CLI 2.60.0 or newer is required; installed version is $($azVersion.'azure-cli')."
}

if ([string]::IsNullOrWhiteSpace($VirtualNetworkSubscriptionId)) {
    $VirtualNetworkSubscriptionId = $SubscriptionId
}
if ([string]::IsNullOrWhiteSpace($KeyVaultSubscriptionId)) {
    $KeyVaultSubscriptionId = $SubscriptionId
}

$restrictedAdministrativeUnits = @()
Write-InstallationSummary -RestrictedAdministrativeUnits $restrictedAdministrativeUnits
if (-not $ValidateOnly -and -not $WhatIfPreference -and -not $Force) {
    if (-not $PSCmdlet.ShouldProcess(
        "$SubscriptionId/$ResourceGroupName",
        'Execute the installation plan shown above'
    )) {
        Write-Host 'Installation canceled before any changes were made.' -ForegroundColor Yellow
        return
    }
}
elseif ($Force -and -not $ValidateOnly -and -not $WhatIfPreference) {
    Write-Host 'Force mode enabled; installation approval and interactive prompts are disabled.' -ForegroundColor Yellow
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

if ($CreateKeyVault) {
    Write-Step 'Validating new Key Vault name'
    $existingVault = Invoke-AzureCli -Arguments @(
        'keyvault', 'show', '--subscription', $KeyVaultSubscriptionId,
        '--resource-group', $KeyVaultResourceGroupName, '--name', $KeyVaultName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($existingVault.ExitCode -eq 0) {
        $vault = $existingVault.Output | ConvertFrom-Json -Depth 100
        $vaultConfiguration = if ($null -ne $vault.PSObject.Properties['properties']) {
            $vault.properties
        }
        else {
            $vault
        }
        Assert-KeyVault -Vault $vaultConfiguration
    }
    else {
        $nameAvailability = Get-AzureJson -Arguments @(
            'keyvault', 'check-name', '--name', $KeyVaultName
        )
        if (-not $nameAvailability.nameAvailable) {
            throw "Key Vault name '$KeyVaultName' is unavailable: $($nameAvailability.message)"
        }
        $vaultConfiguration = $null
    }
}
else {
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
    Assert-KeyVault -Vault $vaultConfiguration
}

Write-Step 'Validating existing app-owned resources and global names'
Assert-ExistingOwnedResource

Write-Step 'Validating Active Directory forest, account, read access, and LDAP TLS certificate'
if ($SkipActiveDirectoryDirectoryChecks) {
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
    $validatedTransport = $null
    $lastTlsError = $null
    foreach ($candidateServer in $script:ActiveDirectoryServerCandidates) {
        if ([HybridDeviceTaggerLdapCertificateValidatorV4]::ValidateTlsEndpoint(
            $candidateServer,
            636,
            $script:TrustedCertificate)) {
            $ActiveDirectoryServer = $candidateServer
            $validatedTransport = 'LDAPS'
            break
        }
        try {
            Assert-LdapStartTlsEndpoint `
                -Server $candidateServer `
                -Port $LdapFallbackPort
            $ActiveDirectoryServer = $candidateServer
            $validatedTransport = 'LDAP StartTLS'
            break
        }
        catch {
            $lastTlsError = $_.Exception
            Write-Warning "Domain controller '$candidateServer' is unavailable through LDAPS and StartTLS; trying the next discovered controller."
        }
    }
    if ([string]::IsNullOrWhiteSpace($validatedTransport)) {
        $errorDetail = if ($null -ne $lastTlsError) {
            $lastTlsError.Message
        }
        else {
            'No discovered endpoint completed a trusted TLS handshake.'
        }
        throw "Neither LDAPS nor LDAP StartTLS certificate validation succeeded for any discovered domain controller. Last error: $errorDetail"
    }
    $script:EffectiveAllowLdapFallback = $validatedTransport -eq 'LDAP StartTLS'
    Write-Warning "Skipped repeated forest and account checks for the existing deployment; strict $validatedTransport endpoint validation succeeded."
}
else {
    Test-LdapConfiguration
}

if ($script:CreateManagementApiApplication -and
    -not $ValidateOnly -and
    -not $WhatIfPreference) {
    Write-Step 'Creating management API Entra application'
    New-ManagementApiApplication
}

$deploymentArtifactId = [guid]::NewGuid()
$parameterFile = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$deploymentArtifactId.parameters.json"
$compiledTemplateFile = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$deploymentArtifactId.template.json"
try {
    $parameterJson = Get-DeploymentParameter | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $parameterFile,
        $parameterJson,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Step 'Compiling Bicep template'
    $null = Invoke-AzureCli -Arguments @(
        'bicep', 'build', '--file', $templatePath,
        '--outfile', $compiledTemplateFile,
        '--only-show-errors'
    ) -TimeoutSeconds 60
    if (-not [System.IO.File]::Exists($compiledTemplateFile)) {
        throw "Bicep compilation did not create the ARM template: $compiledTemplateFile"
    }
    $compiledTemplate = Get-Content -LiteralPath $compiledTemplateFile -Raw |
        ConvertFrom-Json -Depth 100
    $parameterDocument = $parameterJson | ConvertFrom-Json -Depth 100
    $templateParameterNames = @($compiledTemplate.parameters.PSObject.Properties.Name)
    $suppliedParameterNames = @($parameterDocument.parameters.PSObject.Properties.Name)
    $unknownParameters = @($suppliedParameterNames | Where-Object {
        $templateParameterNames -notcontains $_
    })
    if ($unknownParameters.Count -gt 0) {
        throw "Deployment parameters do not exist in the compiled template: $($unknownParameters -join ', '). Ensure Install-HybridDeviceTagger.ps1 and main.bicep come from the same package."
    }
    $missingRequiredParameters = @(
        $compiledTemplate.parameters.PSObject.Properties |
            Where-Object {
                $null -eq $_.Value.PSObject.Properties['defaultValue'] -and
                $suppliedParameterNames -notcontains $_.Name
            } |
            ForEach-Object { $_.Name }
    )
    if ($missingRequiredParameters.Count -gt 0) {
        throw "Required deployment parameters are missing: $($missingRequiredParameters -join ', '). Ensure Install-HybridDeviceTagger.ps1 and main.bicep come from the same package."
    }

    if ($ValidateOnly) {
        Write-Step 'Validating ARM template'
        $null = Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'validate', '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName, '--template-file', $compiledTemplateFile,
            '--parameters', "@$parameterFile", '--validation-level', 'Template',
            '--only-show-errors'
        ) -TimeoutSeconds 300
        Write-Host "Validation completed successfully. No resources were changed." -ForegroundColor Green
        return
    }

    if ($WhatIfPreference) {
        Write-Step 'Running Azure Resource Manager What-if'
        $whatIfResult = Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'what-if', '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName, '--template-file', $compiledTemplateFile,
            '--parameters', "@$parameterFile", '--result-format', 'FullResourcePayloads'
        )
        if (-not [string]::IsNullOrWhiteSpace($whatIfResult.Output)) {
            Write-Host $whatIfResult.Output
        }
        Write-Host 'What-if completed. No resources were changed.' -ForegroundColor Green
        return
    }

    Write-Step 'Configuring management API application roles'
    Set-ManagementApiAuthorization

    Write-Step 'Deploying Azure infrastructure'
    $deploymentName = "hybrid-device-tagger-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
    try {
        $null = Start-AzureGroupDeployment `
            -DeploymentName $deploymentName `
            -TemplateFile $compiledTemplateFile `
            -ParameterFile $parameterFile
    }
    catch {
        $deploymentProbe = Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'show',
            '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName,
            '--name', $deploymentName,
            '--only-show-errors', '--output', 'none'
        ) -AllowFailure -TimeoutSeconds 60
        if ($deploymentProbe.ExitCode -ne 0) {
            throw
        }
        Write-Warning "The ARM submission response was interrupted, but deployment '$deploymentName' exists and will be monitored."
    }
    $script:AzureDeploymentStarted = $true
    Write-Host "Azure deployment '$deploymentName' submitted."
    $deployment = Wait-AzureGroupDeployment -DeploymentName $deploymentName
    $principalId = [string]$deployment.properties.outputs.functionAppPrincipalId.value
    if ([string]::IsNullOrWhiteSpace($principalId)) {
        throw 'The deployment did not return the Function App managed identity principal ID.'
    }

    if ($CreateKeyVault) {
        Write-Step 'Validating new Key Vault configuration'
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
        Assert-KeyVault -Vault $vaultConfiguration
    }

    if (-not $SkipCodeDeployment) {
        Write-Step 'Validating private endpoint DNS resolution'
        Assert-PrivateEndpointDnsResolution
    }

    Write-Step 'Assigning Microsoft Graph application permissions'
    Grant-GraphApplicationPermission -PrincipalId $principalId

    if (-not $SkipCodeDeployment) {
        Write-Step 'Building and deploying Function App code'
        Publish-FunctionApp
    }

    if ($null -ne $ADServiceAccountCredential) {
        Write-Step 'Initializing Active Directory credential through the Function App'
        Initialize-CurrentPasswordThroughFunction
    }
    else {
        Write-Host 'Credential initialization skipped because no ADServiceAccountCredential was supplied.'
    }

    Write-Host "Installation completed for Function App '$FunctionAppName'." -ForegroundColor Green
    Write-Host ''
    Write-Host 'No tagging rules were created during installation.' -ForegroundColor Yellow
    Write-Host 'Add the first rule through the HybridDeviceTagger management module:'
    Write-Host "  Import-Module '$scriptRoot\HybridDeviceTagger.Management'"
    Write-Host "  Connect-HybridDeviceTagger -ApiBaseUri 'https://$FunctionAppName.azurewebsites.net/api/management' -ClientId '$ManagementApiClientId'"
    Write-Host '  Get-Help Add-HybridDeviceTaggerRule -Examples'
    Write-Host '  Add-HybridDeviceTaggerRule -Rule $rule'
}
finally {
    if ([System.IO.File]::Exists($parameterFile)) {
        [System.IO.File]::Delete($parameterFile)
    }
    if ([System.IO.File]::Exists($compiledTemplateFile)) {
        [System.IO.File]::Delete($compiledTemplateFile)
    }
}
}
catch {
    if (-not [string]::IsNullOrWhiteSpace(
            [string]$script:CreatedManagementApiApplicationObjectId) -and
        -not $script:AzureDeploymentStarted) {
        $cleanupResult = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'delete',
            '--url', "https://graph.microsoft.com/v1.0/applications/$($script:CreatedManagementApiApplicationObjectId)",
            '--only-show-errors'
        ) -AllowFailure
        if ($cleanupResult.ExitCode -eq 0) {
            Write-Warning 'Removed the automatically created management API Entra application because installation stopped before Azure deployment.'
        }
        else {
            Write-Warning "The automatically created management API Entra application could not be removed. Reuse client ID '$ManagementApiClientId' when retrying the installation."
        }
    }
    if ($VerbosePreference -eq 'Continue' -or -not $script:CompactInstallerError) {
        Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
    $compactFailure = $true
    $Host.UI.WriteErrorLine($_.Exception.Message)
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript -Confirm:$false | Out-Null
    }
    if (-not $compactFailure) {
        Write-Host "Installer log: $installerLogPath"
    }
}
