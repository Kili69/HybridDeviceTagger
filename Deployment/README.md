# Installation

`Install-HybridDeviceTagger.ps1` validates the environment and installs the Function App by using `main.bicep`. Existing virtual networks, subnets, and Key Vaults are referenced but never created or modified by the template.

## Required permissions

Installation uses separate Azure, Entra, Active Directory, and runtime identities. Do not grant the AD service account Azure or Entra administrative roles.

| Identity | Required permission | Scope and purpose |
| --- | --- | --- |
| Azure installation user | `Contributor` | Target resource group; validates and deploys the Function App, FC1 plan, Storage, monitoring resources, and Storage private endpoint. |
| Azure installation user | `Reader` plus `Microsoft.Network/virtualNetworks/subnets/join/action`, normally provided by `Network Contributor` | Existing VNet, Function integration subnet, and private-endpoint subnet. `Reader` alone is insufficient for attaching the Storage private endpoint. |
| Azure installation user | `Microsoft.Authorization/roleAssignments/write`, normally provided by `Role Based Access Control Administrator`, `User Access Administrator`, or `Owner` | Target Storage account and existing Key Vault; assigns runtime roles to the Function managed identity. Limit the assignment to these resource scopes where possible. |
| Azure installation user during AD bootstrap | `Key Vault Secrets Officer` | Existing Key Vault; creates or replaces the current AD password secret. This temporary assignment can be removed after installation verification. |
| Entra administrator | `Privileged Role Administrator` or `Global Administrator` | Grants Microsoft Graph application roles `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`. For configured restricted management administrative units, also creates and assigns the scoped custom device-extension role after explicit approval. |
| AD validation/runtime service account | Read access to the forest configuration partition, configured OUs and computer attributes; read access to its own account; `SELF / Change Password` | Forest-wide LDAP validation and device discovery. Password changes require LDAPS; the signed and sealed LDAP fallback is read/bind only. |
| AD provisioning identity | Create user in the configured parent OU and enable/reset password on that account | Used only with `-PrepareActiveDirectoryAccountOnly`; it is not stored or used by the Function. Existing delegated AD permissions may be used instead of broad domain administration. |
| DNS administrator | Create a private DNS zone and A record, or modify the existing private DNS service | Required when Storage public network access is disabled. The Function subnet must resolve `<storage-account>.blob.core.windows.net` through `<storage-account>.privatelink.blob.core.windows.net` to the Storage private endpoint IP. |

The management REST API additionally requires a single-tenant Entra app registration whose Application ID URI is `api://<client-id>`. Pass its application/client ID as `ManagementApiClientId`. `ManagementApiAllowedPrincipalObjectId` accepts one to 100 Entra object IDs for users or service principals allowed to call the API; when omitted interactively, the installer restricts access to the signed-in Entra user. Easy Auth validates issuer, audience, authentication and this principal allowlist before a request reaches the Function.

The Azure installation user also needs read access to the current Key Vault secret metadata. The signed-in identity used by Azure CLI must be able to read Microsoft Graph service principals. Assigning `Device.ReadWrite.All` requires an appropriately privileged Entra administrator; Azure subscription roles such as `Owner` do not grant this Entra permission.

After deployment, Bicep grants the Function App's system-assigned managed identity:

- `Storage Blob Data Owner` on the deployment Storage account;
- `Key Vault Secrets Officer` on the configured Key Vault;
- Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`, when the signed-in Entra administrator is authorized to create app-role assignments.

If the Azure deployment user cannot grant Graph application roles, an Entra administrator must perform that step separately for the managed-identity service principal returned by the deployment. Code deployment can proceed after private Storage DNS is working, but device updates will fail until `Device.ReadWrite.All` is assigned.

The script does not persist or pass the AD password to Azure Resource Manager. It uses `ActiveDirectoryCredential` only for preflight directory queries and bind validation. When this parameter is omitted during an interactive run, the installer opens a credential prompt for the configured `ADServiceAccount`. Being signed in as a domain user is sufficient for some discovery queries but does not prove that the Function App's service account can bind with its own password. Noninteractive runs must supply `ActiveDirectoryCredential` explicitly. The current password must already exist in the Key Vault.

Account creation and password resets are isolated in `-PrepareActiveDirectoryAccountOnly`. The script checks the configured service-account DN using a separate `ActiveDirectoryProvisioningCredential`. If the account is absent, it asks before creating and enabling it with a cryptographically randomized complex password. If the account exists, it offers a randomized complex password or secure manual entry before resetting it. The selected password is written directly to the current Key Vault secret without being displayed. If the provisioning credential parameter is omitted, the installer requests it in a local credential dialog. Both operations require LDAPS. The installer explicitly sets `pwdLastSet=-1`, so **User must change password at next logon** is never enabled. Validation, What-if, and normal deployment never create an account or reset a password.

For a new service account, run once with `-PrepareActiveDirectoryAccountOnly`. This mode performs the normal prerequisite checks, creates or resets and validates the AD account, synchronizes the current Key Vault secret, and exits before ARM or code deployment. The installer prefers LDAPS. If LDAPS is unavailable and LDAP fallback is explicitly approved, bootstrap uses the installed Active Directory PowerShell module over Active Directory Web Services against the parameterized `ActiveDirectoryServer`. Then run the mandatory `-ValidateOnly`, What-if, and live installation stages. The installation identity requires temporary `Key Vault Secrets Officer` access for this bootstrap.

## Validation

Run validation first. This checks the Azure account and subscription, CLI and SDK versions, provider registration, FC1 region support, resource names, existing app-owned resources, subnet delegation and size, Key Vault security, the current password secret, LDAPS certificate trust or LDAP fallback availability, forest identity, OU existence, service-account bind, and OU read access.

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
- Existing Key Vault: `kv-devtagger-20260904`;
- Flex Consumption plan: `asp-devtagger-20260904`;
- Log Analytics workspace: `log-devtagger-20260904`;
- Application Insights: `appi-devtagger-20260904`;
- Storage private endpoint: `pep-devtagger-20260904-blob`.

The interactive Storage account prompt derives its default from the selected Function App name, adds the `st` prefix, removes unsupported characters, converts it to lowercase, and truncates the suffix when required by the 24-character Storage limit. The interactive Key Vault prompt similarly adds the `kv-` prefix to the Function App suffix and truncates it to the 24-character Key Vault limit. The Key Vault resource-group prompt defaults to the selected Function App resource group. These prompts accept their defaults with Enter. The Key Vault remains an existing shared resource and is not created or renamed. Existing VNet and subnet names are supplied separately and are not renamed.

The script reads all domain cross-references from the forest configuration partition. An OU-only `BaseDn`, such as `OU=Tier0,OU=Admin`, is resolved and subtree-tested in every forest domain; domains without that path are skipped, while a path absent from the entire forest fails validation. A full DN containing `DC=` remains limited to that domain. Each rule must configure at least one extension attribute, Entra group Object ID, or administrative-unit Object ID. Group and AU targets are limited to 50 unique, nonempty GUIDs per type. The script cannot prove the account's self-service `unicodePwd` change permission without actually rotating the password. Confirm `SELF / Change Password` separately before production installation.

The installer resolves every configured administrative unit and detects restricted management administrative units before deployment. Interactive installation requires typing `YES` before it creates and assigns the custom `HybridDeviceTagger Restricted Device Attribute Writer` role at those exact RMAU scopes. Noninteractive installation requires `-GrantRestrictedAdministrativeUnitRole`. Validation and What-if report RMAU targets without assigning roles.

`-ActiveDirectoryTrustedCertificatePath` optionally accepts a DER or PEM public certificate. Supply the issuing root CA certificate in production, or the directly self-signed DC certificate for a lab. The installer uses it for its LDAPS preflight and writes the validated public certificate into the Function package as PEM without a private key. Runtime validation still requires Server Authentication EKU, a valid chain to that exact trust anchor, and a DNS name matching the LDAP endpoint.

For an update to an existing Function App, `-SkipActiveDirectoryDirectoryChecks` may be used when the deployment identity cannot read the current Key Vault secret and no matching AD credential is available interactively. This still requires `-ActiveDirectoryTrustedCertificatePath` and performs strict LDAPS endpoint validation, but skips the repeated forest, OU, and service-account queries. It is rejected for first-time installations and service-account preparation.

LDAPS remains the primary protocol. The installer always tests it first. If the secure bind succeeds, the deployed Function has LDAP fallback disabled, even when `-AllowLdapFallback` was supplied. If LDAPS fails with a connection, TLS, or availability error, an interactive installation explains the fallback and asks whether to continue with LDAP. The fallback uses Negotiate authentication with LDAP signing and sealing instead of Basic Bind. Declining cancels the installation.

For Azure DevOps or another noninteractive environment, `-AllowLdapFallback` is the explicit consent that replaces the prompt. Without this switch, installation stops when LDAPS is unavailable. Invalid credentials never trigger fallback. Function password changes never use unencrypted LDAP; a due rotation is deferred until LDAPS becomes available. Installer bootstrap can instead use Active Directory Web Services as described above.

## What-if and installation

Replace `-ValidateOnly` with `-WhatIf` to show the ARM changes without applying them. Remove both switches to install. Before the first Azure, Entra, Active Directory, Key Vault, or code change, the installer displays a summary of the selected mode, deployment target, resources, permissions, shared-resource references, and optional actions. It then uses standard PowerShell `ShouldProcess` confirmation for the complete plan. Use `-Confirm` to force the prompt or `-Confirm:$false` only for an already approved unattended installation. Declining exits before any installation change. Validation and What-if display the same summary but do not request approval because they do not apply changes.

The central approval also covers `-RegisterProviders`, `-CreateResourceGroup`, and `-PrepareActiveDirectoryAccountOnly`, ensuring those operations cannot mutate state before the summary and confirmation. LDAP fallback, selection of a replacement AD password, and restricted administrative-unit role assignment retain their additional purpose-specific safety prompts.

An empty interactive required value or an unsuitable interactively selected FC1 integration subnet is requested again rather than terminating the installer. By default, specifying a missing VNet resource group, VNet, integration subnet, or private-endpoint subnet, supplying an unsuitable integration subnet, or selecting a VNet in a different region prints only its actionable exception message. Run the installer with `-Verbose` when the full PowerShell error record, source location, and stack information are needed for those validation errors. Unexpected operational, Azure, Entra, Active Directory, and deployment failures remain terminating errors in both modes so automation can detect them reliably. The installer transcript is closed correctly in both modes.

## Updating tagging rules after installation

Use `Update-TaggingConfiguration.ps1` to change OU actions for an existing Function App, including extension attributes, Entra security groups, and administrative units. The script does not run Bicep or deploy application code. It validates the rule structure and writes the complete configuration through the Entra-authenticated management API with ETag concurrency protection. The rules become effective without restarting the Function App.

Display the current configuration without changing it:

```powershell
./Deployment/Update-TaggingConfiguration.ps1 `
    -FunctionAppAdministratorAccount 'function.admin@contoso.com' `
    -SubscriptionId '00000000-0000-0000-0000-000000000000' `
    -ResourceGroupName 'rg-hybrid-device-tagger-prod' `
    -FunctionAppName 'func-hybrid-device-tagger-prod' `
    -ShowCurrent
```

The readable display is written to the host. The script also emits exactly one `HybridDeviceTagger.TaggingConfiguration` object on the success pipeline for `ShowCurrent`, `AddRule`, and `RemoveRuleName`:

```powershell
$configuration = ./Deployment/Update-TaggingConfiguration.ps1 `
    <same Azure parameters> `
    -ShowCurrent

$configuration.Rules
$configuration.RulesJson
```

The object contains `FunctionAppName`, `ResourceGroupName`, `Rules`, `RulesJson`, and `IsValid`. `Rules` is always an array. For `WhatIf`, the returned object represents the configuration currently persisted in Azure, not the unapplied proposal. After a successful add or remove, it represents the newly persisted configuration.

## Reading management API data

`Show-HybridDeviceTaggerApi.ps1` provides three mutually exclusive, read-only operations. It obtains an Entra token from the active Azure CLI session and emits structured PowerShell objects:

```powershell
$common = @{
    FunctionAppAdministratorAccount = 'function.admin@contoso.com'
    SubscriptionId = '00000000-0000-0000-0000-000000000000'
    ResourceGroupName = 'rg-hybrid-device-tagger-prod'
    FunctionAppName = 'func-hybrid-device-tagger-prod'
}

./Deployment/Show-HybridDeviceTaggerApi.ps1 @common -ShowRules
./Deployment/Show-HybridDeviceTaggerApi.ps1 @common -ShowStatus | Format-List
./Deployment/Show-HybridDeviceTaggerApi.ps1 @common -ShowTrace |
    Format-Table timestampUtc, level, phase, action, outcome
```

`ShowRules` emits one object per effective rule. `ShowStatus` emits the latest device-tagging status. `ShowTrace` parses the latest JSONL trace and emits one object per trace event. The active Entra identity must be included in the Function App Easy Auth principal allowlist.

For an update, always pass the complete desired rule set. Add an OU by adding an item, change a tag value in its `ExtensionAttributes`, or remove an OU by omitting its item. Preview the current and proposed configuration first:

```powershell
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

./Deployment/Update-TaggingConfiguration.ps1 `
    -FunctionAppAdministratorAccount 'function.admin@contoso.com' `
    -SubscriptionId '00000000-0000-0000-0000-000000000000' `
    -ResourceGroupName 'rg-hybrid-device-tagger-prod' `
    -FunctionAppName 'func-hybrid-device-tagger-prod' `
    -TaggingRules $taggingRules `
    -ShowCurrent `
    -WhatIf
```

Remove `-WhatIf` to apply the update. A complete replacement through `TaggingRules` always requires a separate interactive confirmation because omitted rules are deleted. `-Confirm:$false` does not bypass this safeguard. In an approved noninteractive pipeline, add both `-ForceTaggingRulesReplacement` and `-Confirm:$false`. The force switch is accepted only with `TaggingRules`. The signed-in Azure CLI account must match `FunctionAppAdministratorAccount` and needs permission to read and update the Function App configuration and restart the app.

Add one rule while preserving all current named rules. A PowerShell object can be passed through `InputObject`:

```powershell
$newRule = @{
    Name = 'Kiosk devices'
    Description = 'Tags devices located in the kiosk OU.'
    BaseDn = 'OU=Kiosks,OU=Workstations'
    ExtensionAttributes = @{ extensionAttribute1 = 'Kiosk' }
}

./Deployment/Update-TaggingConfiguration.ps1 `
    -FunctionAppAdministratorAccount 'function.admin@contoso.com' `
    -SubscriptionId '00000000-0000-0000-0000-000000000000' `
    -ResourceGroupName 'rg-hybrid-device-tagger-prod' `
    -FunctionAppName 'func-hybrid-device-tagger-prod' `
    -AddRule `
    -InputObject $newRule `
    -WhatIf
```

`InputObject` also accepts a JSON object string. It must describe exactly one rule, not an array:

```powershell
$ruleJson = @'
{
  "name": "Kiosk devices",
  "description": "Tags devices located in the kiosk OU.",
  "baseDn": "OU=Kiosks,OU=Workstations",
  "extensionAttributes": { "extensionAttribute1": "Kiosk" }
}
'@

./Deployment/Update-TaggingConfiguration.ps1 `
    <same Azure parameters> `
    -AddRule `
    -InputObject $ruleJson `
    -WhatIf
```

A single PSObject or JSON string can alternatively be piped into the script:

```powershell
$newRule | ./Deployment/Update-TaggingConfiguration.ps1 `
    <same Azure parameters> `
    -AddRule `
    -WhatIf
```

Without `InputObject`, specify the rule fields individually. One rule can assign multiple extension attributes to the same Base DN. Attribute numbers and values are positional pairs. For a new rule, `BaseDn` is required and `RuleDescription` defaults to `Rule <RuleName>`:

```powershell
./Deployment/Update-TaggingConfiguration.ps1 `
    <same Azure parameters> `
    -AddRule `
    -RuleName 'Kiosk devices' `
    -RuleDescription 'Tags devices located in the kiosk OU.' `
    -BaseDn 'OU=Kiosks,OU=Workstations' `
    -ExtensionAttribute 1, 2 `
    -Value 'Kiosk', 'Tier1' `
    -WhatIf
```

Add membership targets by Object ID, with or without extension attributes:

```powershell
./Deployment/Update-TaggingConfiguration.ps1 `
    <same Azure parameters> `
    -AddRule `
    -RuleName 'Kiosk devices' `
    -EntraGroupId $kioskGroupId, $complianceGroupId `
    -AdministrativeUnitId $kioskAdministrativeUnitId `
    -WhatIf
```

When `AddRule` is called interactively without an action, the script prompts for `A` (extension attribute), `G` (Entra group), `U` (administrative unit), or a combination such as `AGU`. It then collects one or more targets of each selected type. `ExtensionAttribute` is always selected by its number from 1 through 15; for example, `-ExtensionAttribute 1,2 -Value 'Kiosk','Tier1'` maps to `extensionAttribute1=Kiosk` and `extensionAttribute2=Tier1`. Numbers must be unique, and both lists must contain the same number of entries. Supplied values are retained, so partially specified calls prompt only for the remainder. Noninteractive CI execution never prompts and requires either `InputObject` or complete action parameters.

With scalar parameters, `AddRule` updates an existing case-insensitive `RuleName` instead of creating a duplicate. When `BaseDn` or `RuleDescription` is omitted, the existing values are retained. Existing extension attributes are also retained; supplied attribute numbers add new assignments or replace the values of those specific attributes. A new rule name still requires `BaseDn`.

Use `-EntraGroupId` and `-AdministrativeUnitId` with Object ID arrays to replace those target sets on a scalar upsert. Omitting either parameter preserves that existing target set. Passing an empty array clears that target type. The Function records managed extension-attribute values and only memberships that HybridDeviceTagger added in its Blob ownership ledger. When a device leaves all configured OUs, or an action no longer applies through any matching rule, the next tagging run clears attributes that still equal their recorded managed value and removes owned group, AU, and RMAU memberships. Manually changed attributes and pre-existing manual memberships are preserved.

Rule names are limited to 1-128 Unicode letters or numbers plus spaces, dots, underscores, hyphens, and parentheses. Base DNs must contain valid `OU=` components with an optional trailing `DC=` part, for example `OU=Tier0,OU=Admin` or `OU=Tier0,OU=Admin,DC=contoso,DC=com`. A domain root such as `DC=contoso,DC=com` is also accepted. LDAP-special characters in component values must be escaped, for example `OU=Tier0\,Privileged,OU=Admin`. The same checks run in the deployment scripts and during Function App startup validation.

Input sizes are bounded before JSON parsing or Azure/LDAP calls. A configuration supports at most 100 rules and 32,768 characters of serialized rule JSON. Each rule permits a 128-character name, 1,024-character description, 2,048-character Base DN, up to 15 extension attributes, 1,024 characters per attribute value, and 50 unique IDs per membership target type. The installer also applies the documented Azure service limits to resource names and tags; AD and Key Vault settings are checked again during Function App startup.

Remove one rule by its case-insensitive unique name:

```powershell
./Deployment/Update-TaggingConfiguration.ps1 `
    -FunctionAppAdministratorAccount 'function.admin@contoso.com' `
    -SubscriptionId '00000000-0000-0000-0000-000000000000' `
    -ResourceGroupName 'rg-hybrid-device-tagger-prod' `
    -FunctionAppName 'func-hybrid-device-tagger-prod' `
    -RemoveRuleName 'Kiosk devices' `
    -WhatIf
```

Configurations created before rule names were introduced remain readable with `-ShowCurrent`. Migrate them once by supplying the complete rule set with `-TaggingRules`, including `Name` and `Description` for every rule. Add and remove operations reject legacy unnamed rules so they cannot create a partially migrated configuration.

The updater validates rule syntax but cannot verify that an OU exists in Active Directory. Run the next DeviceTagger invocation and inspect Application Insights after applying a change. Removing a rule, removing an action, or moving a computer outside all configured OUs causes the next tagging run to clean the Entra state recorded as owned by HybridDeviceTagger. Retain the previous `Tagging__RulesJson` printed after a successful update when rollback may be required.

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