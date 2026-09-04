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

The Azure installation user also needs read access to the current Key Vault secret metadata. The signed-in identity used by Azure CLI must be able to read Microsoft Graph service principals. Assigning `Device.ReadWrite.All` requires an appropriately privileged Entra administrator; Azure subscription roles such as `Owner` do not grant this Entra permission.

After deployment, Bicep grants the Function App's system-assigned managed identity:

- `Storage Blob Data Owner` on the deployment Storage account;
- `Key Vault Secrets Officer` on the configured Key Vault;
- Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`, when the signed-in Entra administrator is authorized to create app-role assignments.

If the Azure deployment user cannot grant Graph application roles, an Entra administrator must perform that step separately for the managed-identity service principal returned by the deployment. Code deployment can proceed after private Storage DNS is working, but device updates will fail until `Device.ReadWrite.All` is assigned.

The script does not persist or pass the AD password to Azure Resource Manager. It uses the supplied `PSCredential` only for preflight directory queries. The current password must already exist in the Key Vault.

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
    -ExpectedAzureAccount 'deployment.user@contoso.com' `
    -SubscriptionId '00000000-0000-0000-0000-000000000000' `
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
    -ActiveDirectoryBindUsername 'svc-HybridDeviceTagger@contoso.com' `
    -ActiveDirectoryBindUserDistinguishedName 'CN=svc-HybridDeviceTagger,OU=Service Accounts,DC=contoso,DC=com' `
    -ActiveDirectoryCredential $credential `
    -TaggingRules $taggingRules `
    -ValidateOnly
```

The script reads all domain cross-references from the forest configuration partition. An OU-only `BaseDn`, such as `OU=Tier0,OU=Admin`, is resolved and subtree-tested in every forest domain; domains without that path are skipped, while a path absent from the entire forest fails validation. A full DN containing `DC=` remains limited to that domain. Each rule must configure at least one extension attribute, Entra group Object ID, or administrative-unit Object ID. Group and AU targets are limited to 50 unique, nonempty GUIDs per type. The script cannot prove the account's self-service `unicodePwd` change permission without actually rotating the password. Confirm `SELF / Change Password` separately before production installation.

The installer resolves every configured administrative unit and detects restricted management administrative units before deployment. Interactive installation requires typing `YES` before it creates and assigns the custom `HybridDeviceTagger Restricted Device Attribute Writer` role at those exact RMAU scopes. Noninteractive installation requires `-GrantRestrictedAdministrativeUnitRole`. Validation and What-if report RMAU targets without assigning roles.

`-ActiveDirectoryTrustedCertificatePath` optionally accepts a DER or PEM public certificate. Supply the issuing root CA certificate in production, or the directly self-signed DC certificate for a lab. The installer uses it for its LDAPS preflight and writes the validated public certificate into the Function package as PEM without a private key. Runtime validation still requires Server Authentication EKU, a valid chain to that exact trust anchor, and a DNS name matching the LDAP endpoint.

For an update to an existing Function App, `-SkipActiveDirectoryDirectoryChecks` may be used when the deployment identity cannot read the current Key Vault secret and no matching AD credential is available interactively. This still requires `-ActiveDirectoryTrustedCertificatePath` and performs strict LDAPS endpoint validation, but skips the repeated forest, OU, and service-account queries. It is rejected for first-time installations and service-account preparation.

LDAPS remains the primary protocol. The installer always tests it first. If the secure bind succeeds, the deployed Function has LDAP fallback disabled, even when `-AllowLdapFallback` was supplied. If LDAPS fails with a connection, TLS, or availability error, an interactive installation explains the fallback and asks whether to continue with LDAP. The fallback uses Negotiate authentication with LDAP signing and sealing instead of Basic Bind. Declining cancels the installation.

For Azure DevOps or another noninteractive environment, `-AllowLdapFallback` is the explicit consent that replaces the prompt. Without this switch, installation stops when LDAPS is unavailable. Invalid credentials never trigger fallback. Function password changes never use unencrypted LDAP; a due rotation is deferred until LDAPS becomes available. Installer bootstrap can instead use Active Directory Web Services as described above.

## What-if and installation

Replace `-ValidateOnly` with `-WhatIf` to show the ARM changes without applying them. Remove both switches to install. The script requests confirmation before deploying unless called with `-Confirm:$false`.

## Updating tagging rules after installation

Use `Update-TaggingConfiguration.ps1` to change OU actions for an existing Function App, including extension attributes, Entra security groups, and administrative units. The script does not run Bicep or deploy application code. It validates the rule structure, replaces the single `Tagging__RulesJson` app setting atomically, and restarts the Function App so the new configuration is loaded.

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

Use `-EntraGroupId` and `-AdministrativeUnitId` with Object ID arrays to replace those target sets on a scalar upsert. Omitting either parameter preserves that existing target set. Passing an empty array clears that target type. The Function removes obsolete memberships only when its Blob ownership ledger proves that HybridDeviceTagger originally added them; pre-existing manual memberships are never claimed or removed.

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

The updater validates rule syntax but cannot verify that an OU exists in Active Directory. Run the next DeviceTagger invocation and inspect Application Insights after applying a change. Removing a rule or changing a value affects future synchronization only; it does not clear values previously written to Entra devices. Retain the previous `Tagging__RulesJson` printed after a successful update when rollback may be required.

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