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
| Entra administrator | `Privileged Role Administrator` or `Global Administrator` | Grants the Microsoft Graph application role `Device.ReadWrite.All` to the Function managed identity. The permission is assigned to the managed identity, not to a user or the AD service account. The privileged directory role can be removed again after the assignment is verified. |
| AD validation/runtime service account | Read access to the forest configuration partition, configured OUs and computer attributes; read access to its own account; `SELF / Change Password` | Forest-wide LDAP validation and device discovery. Password changes require LDAPS; the signed and sealed LDAP fallback is read/bind only. |
| AD provisioning identity | Create user in the configured parent OU and enable/reset password on that account | Used only with `-PrepareActiveDirectoryAccountOnly`; it is not stored or used by the Function. Existing delegated AD permissions may be used instead of broad domain administration. |
| DNS administrator | Create a private DNS zone and A record, or modify the existing private DNS service | Required when Storage public network access is disabled. The Function subnet must resolve `<storage-account>.blob.core.windows.net` through `<storage-account>.privatelink.blob.core.windows.net` to the Storage private endpoint IP. |

The Azure installation user also needs read access to the current Key Vault secret metadata. The signed-in identity used by Azure CLI must be able to read Microsoft Graph service principals. Assigning `Device.ReadWrite.All` requires an appropriately privileged Entra administrator; Azure subscription roles such as `Owner` do not grant this Entra permission.

After deployment, Bicep grants the Function App's system-assigned managed identity:

- `Storage Blob Data Owner` on the deployment Storage account;
- `Key Vault Secrets Officer` on the configured Key Vault;
- Microsoft Graph `Device.ReadWrite.All`, when the signed-in Entra administrator is authorized to create the app-role assignment.

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
        BaseDn = 'OU=Tier0,OU=Admin'
        ExtensionAttributes = @{
            extensionAttribute1 = 'PAW'
            extensionAttribute2 = 'Tier0'
        }
    },
    @{
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
    -LdapFallbackPort 389 `
    -ActiveDirectoryBindUsername 'svc-HybridDeviceTagger@contoso.com' `
    -ActiveDirectoryBindUserDistinguishedName 'CN=svc-HybridDeviceTagger,OU=Service Accounts,DC=contoso,DC=com' `
    -ActiveDirectoryCredential $credential `
    -TaggingRules $taggingRules `
    -ValidateOnly
```

The script reads all domain cross-references from the forest configuration partition. An OU-only `BaseDn`, such as `OU=Tier0,OU=Admin`, is resolved and subtree-tested in every forest domain; domains without that path are skipped, while a path absent from the entire forest fails validation. A full DN containing `DC=` remains limited to that domain. Each rule may assign one or more of `extensionAttribute1` through `extensionAttribute15`. The script cannot prove the account's self-service `unicodePwd` change permission without actually rotating the password. Confirm `SELF / Change Password` separately before production installation.

LDAPS remains the primary protocol. The installer always tests it first. If the secure bind succeeds, the deployed Function has LDAP fallback disabled, even when `-AllowLdapFallback` was supplied. If LDAPS fails with a connection, TLS, or availability error, an interactive installation explains the fallback and asks whether to continue with LDAP. The fallback uses Negotiate authentication with LDAP signing and sealing instead of Basic Bind. Declining cancels the installation.

For Azure DevOps or another noninteractive environment, `-AllowLdapFallback` is the explicit consent that replaces the prompt. Without this switch, installation stops when LDAPS is unavailable. Invalid credentials never trigger fallback. Function password changes never use unencrypted LDAP; a due rotation is deferred until LDAPS becomes available. Installer bootstrap can instead use Active Directory Web Services as described above.

## What-if and installation

Replace `-ValidateOnly` with `-WhatIf` to show the ARM changes without applying them. Remove both switches to install. The script requests confirmation before deploying unless called with `-Confirm:$false`.

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