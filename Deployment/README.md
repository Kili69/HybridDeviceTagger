# Installation

`Install-HybridDeviceTagger.ps1` validates the environment and installs the Function App by using `main.bicep`. The installation archive includes the prebuilt Function App as `Deployment/HybridDeviceTagger.zip`, so the installation computer does not require the .NET SDK. Use `-FunctionPackagePath` only to deploy a different prebuilt Function ZIP. Existing virtual networks and subnets are referenced but never created or modified. The installer can either reference an existing Key Vault or create a private Key Vault in the Function App resource group.

## Required permissions

Installation uses separate Azure, Entra, Active Directory, and runtime identities. Do not grant the AD service account Azure or Entra administrative roles.

| Identity | Required permission | Scope and purpose |
| --- | --- | --- |
| Azure installation user | `Contributor` | Target resource group; validates and deploys the Function App, FC1 plan, Storage, monitoring resources, and Storage private endpoint. |
| Azure installation user | `Reader` plus `Microsoft.Network/virtualNetworks/subnets/join/action`, normally provided by `Network Contributor` | Existing VNet, Function integration subnet, and private-endpoint subnet. `Reader` alone is insufficient for attaching the Storage private endpoint. |
| Azure installation user | `Microsoft.Authorization/roleAssignments/write`, normally provided by `Role Based Access Control Administrator`, `User Access Administrator`, or `Owner` | Target Storage account and selected Key Vault; assigns runtime roles to the Function managed identity. Limit the assignment to these resource scopes where possible. |
| Entra administrator | `Privileged Role Administrator` or `Global Administrator` | Configures and assigns management API application roles, requires assignment on its Enterprise Application, and grants Microsoft Graph application roles `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`. For configured restricted management administrative units, also creates and assigns the scoped custom device-extension role after explicit approval. |
| AD validation/runtime service account | Read access to the forest configuration partition, configured OUs and computer attributes; read access to its own account; `SELF / Change Password` | Forest-wide LDAP validation and device discovery. Password changes require LDAPS; the signed and sealed LDAP fallback is read/bind only. |
| AD provisioning identity | Create user in the configured parent OU and enable/reset password on that account | Used only with `-PrepareActiveDirectoryAccountOnly`; it is not stored or used by the Function. Existing delegated AD permissions may be used instead of broad domain administration. |
| DNS administrator | Create a private DNS zone and A record, or modify the existing private DNS service | Required when Storage public network access is disabled. The Function subnet must resolve `<storage-account>.blob.core.windows.net` through `<storage-account>.privatelink.blob.core.windows.net` to the Storage private endpoint IP. |

The management REST API additionally requires a single-tenant Entra app registration whose Application ID URI is `api://<client-id>`. Pass its application/client ID as `ManagementApiClientId`. The installer creates five application roles and assigns approved users, security groups, or service principals. **Assignment required** remains disabled so Function App Owners and Contributors can obtain a token without a separate API role. Easy Auth validates issuer, audience, and authentication; the Function returns HTTP `403` unless the principal has an accepted application role or an accepted Function App RBAC assignment.

`ManagementApiAllowedPrincipalObjectId` is the migration/bootstrap input. Its principals receive all five roles unless a role-specific list is supplied through `ManagementApiReaderPrincipalObjectId`, `ManagementApiRuleAdministratorPrincipalObjectId`, `ManagementApiTaggingOperatorPrincipalObjectId`, `ManagementApiPasswordRotationAdministratorPrincipalObjectId`, or `ManagementApiCertificateAdministratorPrincipalObjectId`. Prefer security-group assignments for ongoing administration.

The Function also queries Azure Resource Manager at runtime. A principal with the built-in `Owner` or `Contributor` role on the Function App receives rule-administrator and password-rotation-administrator access automatically. Direct assignments, transitive security-group assignments, and assignments inherited from a parent scope are accepted. No other management API role is implied. The Function managed identity receives `Reader` on its own Function App solely to read these effective role assignments.

| Application role | Permitted operations |
| --- | --- |
| `HybridDeviceTagger.Reader` | Read rules, both statuses, and the latest tagging trace |
| `HybridDeviceTagger.RuleAdministrator` | Read and change tagging rules |
| `HybridDeviceTagger.TaggingOperator` | Read tagging status/trace and start immediate tagging |
| `HybridDeviceTagger.PasswordRotationAdministrator` | Initialize the AD credential, read rotation status, and force password rotation |
| `HybridDeviceTagger.CertificateAdministrator` | Read, add, and remove Active Directory LDAPS trust certificates |

### Managing API role assignments after deployment

For users and security groups, open **Microsoft Entra admin center** > **Enterprise applications** > the HybridDeviceTagger management API > **Users and groups** > **Add user/group**. Select the principal and one role, then repeat for each additional role. Use security groups for ongoing administration where practical.

Microsoft Graph PowerShell supports users, security groups, and service principals. The signed-in Entra administrator needs sufficient directory privileges, such as `Privileged Role Administrator`, and consent for the requested Graph scopes:

```powershell
Install-Module Microsoft.Graph.Authentication -Scope CurrentUser
Connect-MgGraph -Scopes 'Application.Read.All', 'AppRoleAssignment.ReadWrite.All'

$apiClientId = '5e324803-5aed-4e5d-b49f-a4bfceddcda8'
$principalObjectId = '<user, group, or service-principal object ID>'
$roleValue = 'HybridDeviceTagger.CertificateAdministrator'

$api = Invoke-MgGraphRequest -Method GET -Uri (
    "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId eq '$apiClientId'&`$select=id,appRoles"
)
$apiServicePrincipal = @($api.value)[0]
$appRole = @($apiServicePrincipal.appRoles | Where-Object value -eq $roleValue)[0]

$assignmentUri = "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiServicePrincipal.id)/appRoleAssignedTo"
$assignments = @(Invoke-MgGraphRequest -Method GET -Uri $assignmentUri).value
$assignment = @($assignments | Where-Object {
    $_.principalId -eq $principalObjectId -and $_.appRoleId -eq $appRole.id
})[0]

if (-not $assignment) {
    $assignment = Invoke-MgGraphRequest -Method POST -Uri $assignmentUri -Body (@{
        principalId = $principalObjectId
        resourceId = $apiServicePrincipal.id
        appRoleId = $appRole.id
    } | ConvertTo-Json)
}
```

List assignments for a principal and remove the assignment returned by the preceding command:

```powershell
$assignments = @(Invoke-MgGraphRequest -Method GET -Uri $assignmentUri).value

$assignments |
    Where-Object principalId -eq $principalObjectId |
    Select-Object id, principalDisplayName, principalType, appRoleId

$assignmentToRemove = @($assignments | Where-Object {
    $_.principalId -eq $principalObjectId -and $_.appRoleId -eq $appRole.id
})[0]
if (-not $assignmentToRemove) {
    throw "The selected principal does not have role '$roleValue'."
}

Invoke-MgGraphRequest -Method DELETE -Uri (
    "$assignmentUri/$($assignmentToRemove.id)"
)
```

Acquire a new management API token after changing application-role assignments. Reconnect the module and, if Azure CLI still returns a cached token, sign in again. Keep **Assignment required** disabled so the Function can evaluate effective Owner and Contributor assignments. Azure RBAC changes are queried on the next API request and do not require a new API token.

The signed-in identity used by Azure CLI must be able to read Microsoft Graph service principals. Assigning `Device.ReadWrite.All` requires an appropriately privileged Entra administrator; Azure subscription roles such as `Owner` do not grant this Entra permission.

After deployment, Bicep grants the Function App's system-assigned managed identity:

- `Storage Blob Data Owner` on the deployment Storage account;
- `Key Vault Secrets Officer` on the configured Key Vault;
- Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`, when the signed-in Entra administrator is authorized to create app-role assignments.

If the Azure deployment user cannot grant Graph application roles, an Entra administrator must perform that step separately for the managed-identity service principal returned by the deployment. Code deployment can proceed after private Storage DNS is working, but device updates will fail until `Device.ReadWrite.All` is assigned.

The script does not persist or pass the AD password to Azure Resource Manager and does not require the installation identity to have Key Vault data access. It uses `ActiveDirectoryCredential` for preflight directory queries and bind validation, then sends it to the Entra-protected Function API. The Function validates the credential again and writes the initial secret through its managed identity. When this parameter is omitted during an interactive run, the installer opens a credential prompt for the configured `ADServiceAccount`. Noninteractive runs must supply `ActiveDirectoryCredential` explicitly.

Account creation and password resets are isolated in `-PrepareActiveDirectoryAccountOnly`. This mode requires an existing HybridDeviceTagger Function App. The script checks the configured service-account DN using a separate `ActiveDirectoryProvisioningCredential`. If the account is absent, it asks before creating and enabling it with a cryptographically randomized complex password. If the account exists, it offers a randomized complex password or secure manual entry before resetting it. The selected password is sent to the Function API and is not displayed. If the provisioning credential parameter is omitted, the installer requests it in a local credential dialog. Both operations require LDAPS. The installer explicitly sets `pwdLastSet=-1`, so **User must change password at next logon** is never enabled.

To prepare a new service account, first deploy the Function App, then run `-PrepareActiveDirectoryAccountOnly`. This mode creates or resets and validates the AD account, initializes the credential through the Function API, and exits without changing Azure resources. The installer prefers LDAPS. If LDAPS is unavailable and LDAP fallback is explicitly approved, bootstrap uses the installed Active Directory PowerShell module over Active Directory Web Services against the parameterized `ActiveDirectoryServer`.

## Validation

Run validation first. This checks the Azure account and subscription, Azure CLI version, provider registration, FC1 region support, resource names, existing app-owned resources, subnet delegation and size, Key Vault selection and security, LDAPS certificate trust or LDAP fallback availability, forest identity, OU existence, service-account bind, and OU read access.

```powershell
$credential = Get-Credential 'svc-HybridDeviceTagger@contoso.com'
$taggingRules = @(
    @{
        Name = 'Tier 0 workstations'
        Description = 'Tags privileged administrative workstations.'
        BaseDn = 'OU=Tier0,OU=Admin'
        ExtensionAttributes = @{
            extensionAttribute1 = 'PAW'
            extensionAttribute2 = 'Tier0'
        }
    },
    @{
        Name = 'Tier 1 servers'
        Description = 'Tags server devices as Tier 1.'
        BaseDn = 'OU=Server'
        ExtensionAttributes = @{
            extensionAttribute2 = 'Tier1'
        }
    }
)

./Deployment/Install-HybridDeviceTagger.ps1 `
    -ResourceGroupName 'rg-hybrid-device-tagger-prod' `
    -Location 'westeurope' `
    -FunctionAppName 'func-hybrid-device-tagger-prod' `
    -StorageAccountName 'sthybridtaggerprod' `
    -VirtualNetworkName 'vnet-hub-prod' `
    -SubnetName 'snet-functions-flex' `
    -PrivateEndpointSubnetName 'snet-private-endpoints' `
    -VirtualNetworkResourceGroupName 'rg-network-prod' `
    -KeyVaultName 'kv-hybrid-tagger-prod' `
    -KeyVaultResourceGroupName 'rg-security-prod' `
    -ActiveDirectoryForestDomain 'contoso.com' `
    -ActiveDirectoryServer 'dc01.contoso.com' `
    -ActiveDirectoryTrustedCertificatePath 'C:\Certificates\Contoso-Root-CA.cer' `
    -LdapFallbackPort 389 `
    -ADServiceAccount 'svc-HybridDeviceTagger@contoso.com' `
    -ActiveDirectoryBindUserDistinguishedName 'CN=svc-HybridDeviceTagger,OU=Service Accounts,DC=contoso,DC=com' `
    -ActiveDirectoryCredential $credential `
    -ManagementApiClientId '11111111-1111-1111-1111-111111111111' `
    -ManagementApiAllowedPrincipalObjectId '22222222-2222-2222-2222-222222222222' `
    -TaggingRules $taggingRules `
    -ValidateOnly
```

The installer uses the identity authenticated through `az login`. When `SubscriptionId` is omitted interactively, the installer proposes the active subscription shown by `az account show`; press Enter to accept it or enter a different subscription ID. The selected subscription is validated and confirmed by name and ID before any dependent installation inputs are collected or validated. Noninteractive runs must supply `SubscriptionId` explicitly. The Key Vault subscription inherits the resulting target subscription unless separately supplied.

Interactive installation asks whether to use an existing Key Vault or create a new one. Existing-vault mode asks for its subscription, resource group, and name. New-vault mode asks only for the name and always uses the Function App subscription and resource group. It creates an RBAC-enabled Standard vault with soft delete, purge protection, disabled public access, a private endpoint, and private DNS integration. If `privatelink.vaultcore.azure.net` is already linked to the selected VNet, that zone is reused; otherwise the deployment creates and links the zone in the Function App resource group. Only the Function App requires network access to the Key Vault private endpoint.

For noninteractive creation, pass `-CreateKeyVault` and `-KeyVaultName`; omit `KeyVaultResourceGroupName` and `KeyVaultSubscriptionId` because the installer sets both from the Function App deployment target:

```powershell
./Deployment/Install-HybridDeviceTagger.ps1 @parameters `
    -CreateKeyVault `
    -KeyVaultName 'kv-hybrid-tagger-prod' `
    -ValidateOnly
```

When the installer is started without a parameter splat, it asks for the subscription first and then prompts for each remaining required value in installation order. An empty interactive entry for a required value displays a short hint and repeats the same prompt until a value is entered. If `TaggingRules` is omitted, the installer guides the user through creation of the first rule instead of requesting JSON. The proposed defaults are `Rule1`, `1st Tagging Rule`, `OU=Computers,OU=Admin`, and `Tier 0` for `extensionAttribute1`. Fully parameterized calls remain noninteractive.

For an omitted Active Directory forest DNS name, the installer proposes the forest of the logged-on Active Directory user when Windows can resolve it; press Enter to accept it. On a workgroup system or when no logged-on-user forest can be detected, the prompt has no default and requires an explicit value.

When `ActiveDirectoryServer` is omitted, the installer uses `ActiveDirectoryForestDomain` as the LDAP/LDAPS endpoint so Active Directory can select a domain controller through DNS. Supply `ActiveDirectoryServer` only when the forest DNS name is not reachable or a specific domain controller must be used.

The `ADServiceAccount` parameter specifies the UPN used by the Function App to bind to Active Directory. When omitted interactively, the prompt proposes `HybridDeviceTagger@<ActiveDirectoryForestDomain>`; press Enter to accept it or enter another UPN. When the account already exists and is discoverable through Active Directory, the installer resolves its distinguished name from the UPN and does not request `ActiveDirectoryBindUserDistinguishedName`. If the account is absent or automatic lookup is unavailable, the installer requests the distinguished name so it can create or locate the account explicitly. When `-PrepareActiveDirectoryAccountOnly` creates a missing account, both supported creation paths set the fixed description `Service account used by HybridDeviceTagger for Active Directory device discovery and password rotation.` The description is not an installer parameter and cannot be changed interactively.

Network input is collected and validated incrementally. During interactive collection, the installer first proposes the target subscription as the virtual-network subscription; press Enter to use it or enter another subscription ID. A VNet in another subscription is supported when it belongs to the same Entra tenant and the deployment identity has the required read and subnet-join permissions there. The installer then asks for and validates the virtual-network resource group, VNet, FC1 integration subnet, and private-endpoint subnet in that order before collecting Key Vault or Active Directory values. A missing interactively entered resource group, VNet, or subnet is reported and requested again. An interactively selected integration subnet that fails the FC1 requirements is also reported and requested again. A missing or unsuitable network resource supplied through `VirtualNetworkResourceGroupName`, `VirtualNetworkName`, `SubnetName`, or `PrivateEndpointSubnetName` terminates the run. The VNet and Function App must use the same Azure region; a mismatch terminates with a concise message identifying both regions and the available corrections. The integration subnet must use IPv4, meet the FC1 prefix requirements, be delegated exclusively to `Microsoft.App/environments`, contain no service endpoints or private endpoints, and differ from the private-endpoint subnet. The private-endpoint subnet must use IPv4 and must not be delegated.

The interactive resource-group prompt proposes `rg-HybridDeviceTagger`; press Enter to accept it or enter another resource group name.

The interactive location prompt proposes the Azure region `westeurope`; press Enter to accept it or enter another FC1-supported region.

The interactive Function App prompt proposes `func-devtagger-yyyyMMdd` using the current UTC date, for example `func-devtagger-20260904`; press Enter to accept it or enter another globally unique name.

Resource-name defaults follow the same workload-and-date naming scheme. For the default Function App name `func-devtagger-20260904`, the installer and Bicep template use:

- Storage account: `stdevtagger20260904` (without hyphens because Azure Storage permits only lowercase letters and numbers);
- Key Vault: `kv-devtagger-20260904`;
- Flex Consumption plan: `asp-devtagger-20260904`;
- Log Analytics workspace: `log-devtagger-20260904`;
- Application Insights: `appi-devtagger-20260904`;
- Storage private endpoint: `pep-devtagger-20260904-blob`.

The interactive Storage account prompt derives its default from the selected Function App name, adds the `st` prefix, removes unsupported characters, converts it to lowercase, and truncates the suffix when required by the 24-character Storage limit. The interactive Key Vault prompt similarly adds the `kv-` prefix to the Function App suffix and truncates it to the 24-character Key Vault limit. In existing-vault mode, its subscription and resource-group prompts default to the Function App deployment target. In new-vault mode, those values are fixed to that target. Existing VNet and subnet names are supplied separately and are not renamed.

The script reads all domain cross-references from the forest configuration partition. An OU-only `BaseDn`, such as `OU=Tier0,OU=Admin`, is resolved and subtree-tested in every forest domain; domains without that path are skipped, while a path absent from the entire forest fails validation. A full DN containing `DC=` is accepted only when its derived DNS domain exactly matches a `dnsRoot` advertised by the configured forest. Foreign, lookalike, and suffix-only domain matches are rejected before persistence and checked again before every tagging search, so rule data cannot redirect an authenticated LDAP bind outside the forest. Each rule must configure at least one extension attribute, Entra group Object ID, or administrative-unit Object ID. Group and AU targets are limited to 50 unique, nonempty GUIDs per type. The script cannot prove the account's self-service `unicodePwd` change permission without actually rotating the password. Confirm `SELF / Change Password` separately before production installation.

The installer resolves every configured administrative unit and detects restricted management administrative units before deployment. Interactive installation requires typing `YES` before it creates and assigns the custom `HybridDeviceTagger Restricted Device Attribute Writer` role at those exact RMAU scopes. Noninteractive installation requires `-GrantRestrictedAdministrativeUnitRole`. Validation and What-if report RMAU targets without assigning roles.

`-ActiveDirectoryTrustedCertificatePath` optionally accepts a DER or PEM public certificate. Supply the issuing root CA certificate in production, or the directly self-signed DC certificate for a lab. The installer uses it for its LDAPS preflight and writes the validated public certificate into the Function package as PEM without a private key. Runtime validation still requires Server Authentication EKU, a valid chain to that exact trust anchor, and a DNS name matching the LDAP endpoint.

For an update to an existing Function App, `-SkipActiveDirectoryDirectoryChecks` may be used when no matching AD credential is available interactively. This still requires `-ActiveDirectoryTrustedCertificatePath` and performs strict LDAPS endpoint validation, but skips the repeated forest, OU, and service-account queries. It is rejected for first-time installations and service-account preparation.

LDAPS remains the primary protocol. The installer always tests it first. If the secure bind succeeds, the deployed Function has LDAP fallback disabled, even when `-AllowLdapFallback` was supplied. If LDAPS fails with a connection, TLS, or availability error, an interactive installation explains the fallback and asks whether to continue with LDAP. The fallback uses Negotiate authentication with LDAP signing and sealing instead of Basic Bind. Declining cancels the installation.

For Azure DevOps or another noninteractive environment, `-AllowLdapFallback` is the explicit consent that replaces the prompt. Without this switch, installation stops when LDAPS is unavailable. Invalid credentials never trigger fallback. Function password changes never use unencrypted LDAP; a due rotation is deferred until LDAPS becomes available. Installer bootstrap can instead use Active Directory Web Services as described above.

## What-if and installation

Replace `-ValidateOnly` with `-WhatIf` to show the ARM changes without applying them. Remove both switches to install. Before the first Azure, Entra, Active Directory, Key Vault, or code change, the installer displays a summary of the selected mode, deployment target, resources, permissions, shared-resource references, and optional actions. It then uses standard PowerShell `ShouldProcess` confirmation for the complete plan. Use `-Confirm` to force the prompt or `-Confirm:$false` only for an already approved unattended installation. Declining exits before any installation change. Validation and What-if display the same summary but do not request approval because they do not apply changes.

The central approval also covers `-RegisterProviders`, `-CreateResourceGroup`, and `-PrepareActiveDirectoryAccountOnly`, ensuring those operations cannot mutate state before the summary and confirmation. LDAP fallback, selection of a replacement AD password, and restricted administrative-unit role assignment retain their additional purpose-specific safety prompts.

An empty interactive required value or an unsuitable interactively selected FC1 integration subnet is requested again rather than terminating the installer. By default, specifying a missing VNet resource group, VNet, integration subnet, or private-endpoint subnet, supplying an unsuitable integration subnet, or selecting a VNet in a different region prints only its actionable exception message. Run the installer with `-Verbose` when the full PowerShell error record, source location, and stack information are needed for those validation errors. Unexpected operational, Azure, Entra, Active Directory, and deployment failures remain terminating errors in both modes so automation can detect them reliably. The installer transcript is closed correctly in both modes.

## Updating tagging rules after installation

Import `HybridDeviceTagger.Management` and connect once to reuse the API URI and delegated Azure CLI token throughout the current PowerShell session. The production API URI and non-secret Entra audience are defaults; pass `ApiBaseUri` or `ClientId` to `Connect-HybridDeviceTagger` for another deployment.

```powershell
Import-Module ./Deployment/HybridDeviceTagger.Management
Connect-HybridDeviceTagger

Get-HybridDeviceTaggerRule
```

After the first PowerShell Gallery release, install the same module without cloning the repository:

```powershell
Install-PSResource -Name HybridDeviceTagger.Management -Repository PSGallery
Import-Module HybridDeviceTagger.Management
Connect-HybridDeviceTagger
```

Every public cmdlet includes built-in synopsis, parameter, output, and example documentation. Use
`Get-Help <command> -Full` or `Get-Help <command> -Examples`, for example:

```powershell
Get-Help Add-HybridDeviceTaggerRule -Full
Get-Help Get-HybridDeviceTaggerTrace -Examples
```

Manage public LDAPS trust anchors without redeploying or restarting the Function App:

```powershell
Get-HybridDeviceTaggerTrustedCertificate
Add-HybridDeviceTaggerTrustedCertificate -Path ./contoso-root-ca.cer
Remove-HybridDeviceTaggerTrustedCertificate -CertificateThumbprint $oldThumbprint -Confirm
```

Certificates may be DER or PEM encoded. The private `run-traces/active-directory-trust-certificates.json` Blob stores only public DER data. Mutations use ETags, duplicate additions are idempotent, and removal of the final certificate is rejected. The certificate packaged during installation remains the fallback until the first API mutation. Every new LDAP connection uses the current Blob-backed collection.

Add or replace one named rule by passing the complete rule object. The API validates it and uses the latest ETag to prevent concurrent updates:

```powershell
$rule = @{
    name = 'Kiosk devices'
    description = 'Tags devices located in the kiosk OU.'
    baseDn = 'OU=Kiosks,OU=Workstations'
    extensionAttributes = @{ extensionAttribute1 = 'Kiosk' }
    entraGroupIds = @()
    administrativeUnitIds = @()
}

Add-HybridDeviceTaggerRule -Rule $rule -WhatIf
Add-HybridDeviceTaggerRule -Rule $rule
```

Remove one rule by its case-insensitive name:

```powershell
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -WhatIf
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices'
```

Add or replace multiple rules by passing an object with a `Rules` property. Existing rules not included in the object are preserved:

```powershell
$configuration = @{
    Rules = @($kioskRule, $workstationRule)
}

Add-HybridDeviceTaggerRule -Rule $configuration -WhatIf
Add-HybridDeviceTaggerRule -Rule $configuration
```

Changes become effective without restarting the Function App. Rule names are case-insensitive and limited to 128 characters. Each rule requires a name, description, valid OU or domain Base DN, and at least one extension attribute, Entra group, or administrative unit. A configuration supports at most 100 rules. Each rule supports up to 15 extension attributes and 50 unique IDs per membership target type.

## Reading management API data

All read operations return structured PowerShell objects:

```powershell
Get-HybridDeviceTaggerRule
Get-HybridDeviceTaggerLastRunStatus
Get-HybridDeviceTaggerPasswordRotationStatus
$trace = @(Get-HybridDeviceTaggerTrace)
```

`Get-HybridDeviceTaggerTrace` displays each event as one logical `<time>,<severity>,<phase>,<status>,<action>,<details>` line while emitting complete `HybridDeviceTagger.TraceEvent` objects. Assignment, filtering, property selection, JSON conversion, and CSV export therefore retain all fields. Long details may wrap visually at the terminal width but contain no embedded newline.

Run operational actions directly through the module:

```powershell
$password = Read-Host 'AD service-account password' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential -Password $password -Confirm
Invoke-HybridDeviceTagging
Invoke-HybridDeviceTaggerPasswordRotation -Confirm
```

The initialization command validates the supplied password against Active Directory and creates the current secret through the Function managed identity; it never replaces an existing current secret. The password-rotation command has high confirmation impact and forces a real service-account password change. It bypasses only the configured age threshold; current credential verification, LDAPS, staging, validation, and secret promotion still apply. Immediate operations return their invocation ID and result synchronously. A second invocation of the same operation receives HTTP `409` while the first is running. The active Entra identity needs the endpoint's application role but does not require Azure Resource Manager or Key Vault data access.

For an interactive account bootstrap, either pass the provisioning credential separately or let the installer prompt for it:

```powershell
$provisioningCredential = Get-Credential -Message 'AD account with create/reset permission'

./Deployment/Install-HybridDeviceTagger.ps1 `
    <same parameters as validation> `
    -ActiveDirectoryProvisioningCredential $provisioningCredential `
    -PrepareActiveDirectoryAccountOnly
```

Account creation defaults to **No**. Password reset offers **Randomized**, **Enter**, and **Cancel**, with **Cancel** as the default. `-InitialPasswordLength` controls randomized bootstrap passwords and defaults to 40 characters.

Use `-RegisterProviders` to register missing providers during a real installation, and `-CreateResourceGroup` to create a missing target resource group. These switches do not make changes in validation or What-if mode. Use `-SkipCodeDeployment` when infrastructure and application deployment are separate pipeline stages.

Use `-PrepareActiveDirectoryAccountOnly` only for the prerequisite bootstrap described above. It does not deploy Azure resources or application code.

For noninteractive Azure DevOps execution, authenticate Azure CLI before invoking the script, provide all parameters explicitly, construct `PSCredential` from a secret pipeline variable, and add `-Confirm:$false`. Do not write the password or the credential object to logs.

## Existing-resource requirements

- The VNet and FC1 Function App must use the same Azure region.
- DNS must resolve every domain `dnsRoot` returned by the forest configuration partition from the Function integration subnet.
- Network routing and firewalls must allow LDAPS port 636 and, when fallback is enabled, LDAP port 389 to every affected forest domain.
- The subnet must be at least `/27`, have no underscore in its name, and be delegated only to `Microsoft.App/environments`.
- The subnet must not contain service endpoints or private endpoints.
- The separate private-endpoint subnet must permit private endpoints. The template creates a private endpoint named `pe-<storage-account>-blob` in this subnet.
- Private DNS must resolve `<storage-account>.blob.core.windows.net` to the Storage private endpoint IP through the `privatelink.blob.core.windows.net` zone before code deployment.
- The Key Vault must use RBAC, soft delete, and purge protection.
- A network-restricted Key Vault must have an approved private endpoint and DNS/routing reachable from the Function App subnet.
- The Function App managed identity receives `Storage Blob Data Owner`, `Key Vault Secrets Officer`, and Microsoft Graph `Device.ReadWrite.All`.

The installer is idempotent: Bicep converges existing compatible resources, deterministic role-assignment names prevent duplicates, and the Graph role is checked before creation.

## Troubleshooting

### Installer transcript

Every `Install-HybridDeviceTagger.ps1` invocation replaces `HybridDeviceTagger-Install.log` in the current user's temporary directory. The PowerShell transcript captures the console workflow for validation, What-if, Active Directory preparation, and installation and is closed on success, failure, cancellation, or an early operational return.

```powershell
$installerLog = Join-Path ([System.IO.Path]::GetTempPath()) 'HybridDeviceTagger-Install.log'
code $installerLog
```

The transcript can contain user, device, resource, and directory names and must be treated as confidential. The script does not print passwords; do not write credentials to the console during manual troubleshooting.

### Latest-run diagnostic trace

Each `DeviceTaggerFunction` invocation replaces `device-tagger-last-run.jsonl` in the private `run-traces` Blob container. Actions are appended immediately as JSON Lines with a UTC timestamp, sequence, invocation ID, phase, action, outcome, device and target IDs, and structured exception details. Extension-attribute values, passwords, and tokens are not written; attribute comparisons use SHA-256 fingerprints. The fixed latest-run file is intended for direct analysis in VS Code and remains useful when a run fails after only part of the workflow completed.

Download requires Blob data-plane read permission and network access to the private Storage endpoint:

```powershell
az storage blob download `
    --account-name '<storage-account>' `
    --container-name 'run-traces' `
    --name 'device-tagger-last-run.jsonl' `
    --file '.\device-tagger-last-run.jsonl' `
    --auth-mode login

code '.\device-tagger-last-run.jsonl'
```

The exported trace can then be opened directly in VS Code and provided to Copilot for failure analysis.