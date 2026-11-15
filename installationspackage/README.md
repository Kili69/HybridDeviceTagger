# Installation

This directory contains every installation file. A release archive `HybridDeviceTagger-<version>.zip` contains this directory together with the prebuilt Function App `function/HybridDeviceTagger.zip`, `version.json`, and `LICENSE`.

| File | Purpose |
| --- | --- |
| `Install.ps1` | Guided installer and interactive entry point |
| `Install-HybridDeviceTagger.ps1` | Validation and installation engine, used by `Install.ps1` and directly for unattended or pipeline installations |
| `main.bicep`, `key-vault-role-assignment.bicep` | Azure infrastructure |
| `function/HybridDeviceTagger.zip` | Prebuilt Function App (build output) |
| `HybridDeviceTagger.Management/` | PowerShell management module |
| `version.json` | Product version, branch, and commit of the package (build output) |

`Install-HybridDeviceTagger.ps1` validates the environment and installs the Function App by using `main.bicep`. It deploys the prebuilt Function App from `-FunctionPackagePath`, which defaults to `function/HybridDeviceTagger.zip` next to the engine, and stops when that file is missing. The installation computer therefore does not require the .NET SDK. When installing from a repository checkout, create the package first with `scripts/Build-InstallationPackage.ps1`. Use `-FunctionPackagePath` only to deploy a different prebuilt Function ZIP. Existing virtual networks and subnets are referenced but never created or modified. The installer can either reference an existing Key Vault or create a private Key Vault in the Function App resource group.

## Guided installation

Run `Install.ps1` from the extracted package after `az login`:

```powershell
./Install.ps1
./Install.ps1 -Mode Validate -UseSavedSettings
./Install.ps1 -Mode Install -UseSavedSettings
```

`Install.ps1` first checks Azure CLI, the ActiveDirectory module, the prebuilt Function package, and the Azure CLI sign-in, offering `az login` when required. It then asks for the installation settings with defaults and format validation: the Azure target, the existing network, a new or existing Key Vault, the Active Directory forest, an optional domain controller used only for installer validation, the service-account UPN, an optional distinguished name, the required LDAPS root CA certificate path, an optional management API client ID (empty creates the registration or reuses the one of an existing installation), both schedules, and provider registration. Enter `-` to clear an optional saved value.

**The public root CA certificate (.cer/.pem) is mandatory for installation, validation, and updates, including when local AD DNS is unavailable.** The installer imports it into the Azure Function package for both LDAPS and StartTLS. Importing it only into the installation PC's certificate store is not sufficient. Do not supply a private key. A missing or empty certificate path in saved settings is requested again; direct engine calls must supply `-ActiveDirectoryTrustedCertificatePath` and fail before Azure access when it is omitted.

At the guided certificate prompt, paths containing spaces can be entered with or without matching single or double quotes, for example `"C:\Certificates\Contoso Root CA.cer"`. Surrounding quotes are removed from new and saved answers before resolving the file.

The guided installer does not ask for a management principal object ID or assign bootstrap application roles. Function App Owners and Contributors can administer rules and password rotation by default. A `ManagementApiAllowedPrincipalObjectId` saved by an earlier guided installer is ignored and omitted when settings are saved again; existing application-role assignments are retained. Additional users and groups can be assigned roles after deployment as described below, with the required Entra permissions.

The answers are stored without secrets in `installation-settings.json` next to the script and offered as defaults on the next run; `-UseSavedSettings` accepts them without asking. The package build never includes this file. `-Mode Install|Validate|WhatIf` selects the action without asking. The script asks once for the service-account password through `Get-Credential` and passes it in memory as `ADServiceAccountCredential`. `Install` runs the read-only validation, optionally shows Azure What-If, asks `Start the installation now?`, and then runs the engine, which displays its summary and requests the final confirmation.

## Required permissions

Installation uses separate Azure, Entra, Active Directory, and runtime identities. Do not grant the AD service account Azure or Entra administrative roles.

| Identity | Required permission | Scope and purpose |
| --- | --- | --- |
| Azure installation user | `Contributor` | Target resource group; validates and deploys the Function App, FC1 plan, Storage, monitoring resources, and the Blob and Queue Storage private endpoints. |
| Azure installation user | `Reader` plus `Microsoft.Network/virtualNetworks/subnets/join/action`, normally provided by `Network Contributor` | Existing VNet, Function integration subnet, and private-endpoint subnet. `Reader` alone is insufficient for attaching the Storage private endpoints. |
| Azure installation user | `Microsoft.Authorization/roleAssignments/write`, normally provided by `Role Based Access Control Administrator`, `User Access Administrator`, or `Owner` | Target Storage account, Function App, and selected Key Vault or its two password secrets; assigns runtime roles to the Function managed identity and removes superseded ones. Limit the assignment to these resource scopes where possible. |
| Azure installation user | Control-plane permission to create Key Vault secrets (`Microsoft.KeyVault/vaults/secrets/write`), for example `Key Vault Contributor` or `Contributor` on the vault | Existing Key Vault only; the deployment creates missing password secrets as unusable placeholders so the managed identity can be authorized on these two secrets only. No Key Vault data-plane access is required. |
| Entra administrator | `Privileged Role Administrator` or `Global Administrator` | Creates the management API app registration and Enterprise Application during a new installation, configures and assigns its application roles, and grants Microsoft Graph application roles `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`. For rules added later that target restricted management administrative units, separately creates and assigns the required scoped custom device-extension role. |
| AD validation/runtime service account | Read access to the forest configuration partition, configured OUs and computer attributes; read access to its own account; `SELF / Change Password` | Forest-wide LDAP validation and device discovery through LDAPS or LDAP with StartTLS. |
| DNS administrator | Read existing private DNS zones linked to the VNet, or create the deployment-owned private DNS zone; configure conditional forwarding when the VNet uses custom DNS | The installer creates or reuses the private DNS zones and manages the private-endpoint A records. Custom DNS servers must forward `privatelink.blob.core.windows.net`, `privatelink.queue.core.windows.net`, and, for a private Key Vault, `privatelink.vaultcore.azure.net` to Azure DNS `168.63.129.16`. |

The management REST API uses a single-tenant Entra app registration whose Application ID URI is `api://<client-id>`. During a new installation, omit `ManagementApiClientId`; after installation approval the installer creates the app registration, its Enterprise Application, the identifier URI, a delegated `access_as_user` scope, and four application roles automatically. Microsoft Azure CLI is preauthorized only for this delegated scope so the installer can obtain the signed-in user's token without a separate consent login. API operations still require an assigned application role or accepted Function App RBAC role. `-ValidateOnly` and `-WhatIf` remain read-only and only report the planned registration. When resuming or updating an existing Function App, the installer reuses `ManagementApi__ClientId` from its application settings. Supply `ManagementApiClientId` explicitly only when that setting is unavailable or when a new deployment must reuse a centrally managed registration.

The installer assigns users, security groups, or service principals to application roles only when explicitly supplied through the engine parameters. It does not automatically assign roles to the signed-in user. **Assignment required** remains disabled so Function App Owners and Contributors can obtain a token without a separate API role. Easy Auth validates issuer, audience, and authentication; the Function returns HTTP `403` unless the principal has an accepted application role or an accepted Function App RBAC assignment.

New app registrations start with empty delegated scopes and Azure CLI preauthorizations. The installer creates the required missing entries and application roles while preserving unrelated entries; reruns leave matching settings unchanged. A missing required application, enterprise application, or Graph permission is reported explicitly.

`ManagementApiAllowedPrincipalObjectId` is an optional migration/bootstrap input for direct engine calls. Its principals receive all four roles unless a role-specific list is supplied through `ManagementApiReaderPrincipalObjectId`, `ManagementApiRuleAdministratorPrincipalObjectId`, `ManagementApiTaggingOperatorPrincipalObjectId`, or `ManagementApiPasswordRotationAdministratorPrincipalObjectId`. Omit these parameters to rely on Function App Owner/Contributor administration without additional application-role assignments. Prefer security-group assignments for ongoing administration.

The former `HybridDeviceTagger.CertificateAdministrator` role and the `ManagementApiCertificateAdministratorPrincipalObjectId` parameter have been removed together with the runtime trusted-certificate store. During an update, the installer removes all assignments of that role, disables it, and deletes it from the app registration.

The Function also queries Azure Resource Manager at runtime. A principal with the built-in `Owner` or `Contributor` role on the Function App receives rule-administrator and password-rotation-administrator access automatically. Direct assignments, transitive security-group assignments, and assignments inherited from a parent scope are accepted. No other management API role is implied. The Function managed identity receives `Reader` on its own Function App solely to read these effective role assignments.

| Application role | Permitted operations |
| --- | --- |
| `HybridDeviceTagger.Reader` | Read rules, both statuses, and the latest tagging trace |
| `HybridDeviceTagger.RuleAdministrator` | Read and change tagging rules; only Function App Owners/Contributors may add new Entra group or administrative-unit targets |
| `HybridDeviceTagger.TaggingOperator` | Read tagging status/trace and start immediate tagging |
| `HybridDeviceTagger.PasswordRotationAdministrator` | Initialize the AD credential, read rotation status, and force password rotation |

### Managing API role assignments after deployment

For users and security groups, open **Microsoft Entra admin center** > **Enterprise applications** > the HybridDeviceTagger management API > **Users and groups** > **Add user/group**. Select the principal and one role, then repeat for each additional role. Use security groups for ongoing administration where practical.

Function App Owner/Contributor access alone does not grant permission to manage Entra application-role assignments. These administrators need the Entra privileges described below to add API users or groups.

Microsoft Graph PowerShell supports users, security groups, and service principals. The signed-in Entra administrator needs sufficient directory privileges, such as `Privileged Role Administrator`, and consent for the requested Graph scopes:

```powershell
Install-Module Microsoft.Graph.Authentication -Scope CurrentUser
Connect-MgGraph -Scopes 'Application.Read.All', 'AppRoleAssignment.ReadWrite.All'

$apiClientId = '5e324803-5aed-4e5d-b49f-a4bfceddcda8'
$principalObjectId = '<user, group, or service-principal object ID>'
$roleValue = 'HybridDeviceTagger.Reader'

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

- `Storage Blob Data Contributor` and `Storage Queue Data Contributor` on the deployment Storage account;
- `Key Vault Secrets Officer` on the two password secrets (`hybrid-device-tagger-ad-password-current` and `hybrid-device-tagger-ad-password-next`) of an existing Key Vault, or on the whole vault when it was created with `-CreateKeyVault` and is dedicated to HybridDeviceTagger;
- `Reader` on its own Function App, used only to evaluate effective Owner and Contributor assignments;
- Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`, when the signed-in Entra administrator is authorized to create app-role assignments.

The installer removes superseded assignments of earlier versions: `Storage Blob Data Owner` on the Storage account and, for an existing vault, the vault-wide `Key Vault Secrets Officer` assignment.

For an existing Key Vault, a secret-scoped role assignment requires the secret to exist. The deployment therefore creates each missing password secret as an unusable random placeholder with content type `application/vnd.hybriddevicetagger.placeholder`. Existing secrets are never overwritten. The Function never uses a placeholder as a credential; credential initialization replaces only a missing secret or a placeholder, never a real password.

If the Azure deployment user cannot grant Graph application roles, an Entra administrator must perform that step separately for the managed-identity service principal returned by the deployment. Code deployment can proceed after private Storage DNS is working, but device updates will fail until `Device.ReadWrite.All` is assigned.

The script does not persist or pass the AD password to Azure Resource Manager and does not require the installation identity to have Key Vault data access. It uses `ADServiceAccountCredential` for preflight directory queries and bind validation, then sends it to the Entra-protected Function API. The Function validates the credential again and writes the initial secret through its managed identity, replacing only a missing secret or a deployment placeholder. When this parameter is omitted during an interactive run, the installer opens a credential prompt for the configured `ADServiceAccount`. Noninteractive runs must supply `ADServiceAccountCredential` explicitly.

The AD service account must exist before installation. The installer never creates the account or resets its password. It validates the supplied UPN and credential, resolves the distinguished name, and initializes the current credential through the Function API after deployment. Ensure **User must change password at next logon** is disabled before running the installer.

## Validation

Run validation first. This checks the Azure account and subscription, Azure CLI version, provider registration, FC1 region support, resource names, existing app-owned resources, subnet delegation and size, Key Vault selection and security, LDAPS certificate trust or LDAP fallback availability, forest identity, and the service-account bind.

```powershell
$credential = Get-Credential 'svc-HybridDeviceTagger@contoso.com'

./Install-HybridDeviceTagger.ps1 `
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
    -ADServiceAccountCredential $credential `
    -ValidateOnly
```

The example represents a new installation, so the management API application is created automatically. Add `-ManagementApiClientId '<existing-client-id>'` when updating an existing deployment or deliberately reusing an existing registration.

The installer uses the identity authenticated through `az login`. When `SubscriptionId` is omitted interactively, the installer proposes the active subscription shown by `az account show`; press Enter to accept it or enter a different subscription ID. The selected subscription is validated and confirmed by name and ID before any dependent installation inputs are collected or validated. Noninteractive runs must supply `SubscriptionId` explicitly. The Key Vault subscription inherits the resulting target subscription unless separately supplied.

Interactive installation asks whether to use an existing Key Vault or create a new one. Existing-vault mode asks for its subscription, resource group, and name. New-vault mode asks only for the name and always uses the Function App subscription and resource group. It creates an RBAC-enabled Standard vault with soft delete, purge protection, disabled public access, a private endpoint, and private DNS integration. If `privatelink.vaultcore.azure.net` is already linked to the selected VNet, that zone is reused; otherwise the deployment creates and links the zone in the Function App resource group. Only the Function App requires network access to the Key Vault private endpoint.

For noninteractive creation, pass `-CreateKeyVault` and `-KeyVaultName`; omit `KeyVaultResourceGroupName` and `KeyVaultSubscriptionId` because the installer sets both from the Function App deployment target:

```powershell
./Install-HybridDeviceTagger.ps1 @parameters `
    -CreateKeyVault `
    -KeyVaultName 'kv-hybrid-tagger-prod' `
    -ValidateOnly
```

When the installer is started without a parameter splat, it asks for the subscription first and then prompts for each remaining required value in installation order. An empty interactive entry for a required value displays a short hint and repeats the same prompt until a value is entered. The installer does not request or create tagging rules. After installation, add the first rule through the `HybridDeviceTagger.Management` module. Fully parameterized calls remain noninteractive.

For an omitted Active Directory forest DNS name, the installer proposes the forest of the logged-on Active Directory user when Windows can resolve it; press Enter to accept it. On a workgroup system or when no logged-on-user forest can be detected, the prompt has no default and requires an explicit value.

When `ActiveDirectoryServer` is omitted, the installer resolves `_ldap._tcp.dc._msdcs.<ActiveDirectoryForestDomain>` and validates a discovered domain controller. Supply `ActiveDirectoryServer` only as an installer-validation override. The deployed Function receives the domain, not that override, and performs DNS SRV discovery for every new connection.

If that initial lookup fails or returns no SRV targets on the installation computer, the installer issues a warning instead of stopping. This is expected on a public-network computer without access to internal AD DNS; it can also indicate a misspelled forest name. Local forest, account-DN, and LDAP/TLS preflight checks are deferred, not reported as successful. The Function must still resolve and reach AD from its VNet, validate TLS, and bind with the supplied credential before storing it in Key Vault. Public Azure DNS is not a replacement for internal AD DNS.

An omitted service-account DN is resolved from its UPN across the forest by the Function before password rotation. An explicitly supplied DN remains in use. Azure deployment errors, invalid certificate files, and failed runtime credential initialization are still terminating errors. A failed initialization can leave the deployed management API available: correct the domain as shown below, fix VNet DNS/routing if necessary, then initialize the credential again. Custom private-endpoint DNS checks also need connectivity from the installation computer to the configured VNet DNS servers; a public-network computer is not guaranteed to complete every deployment check.

`ADServiceAccountCredential` contains the existing AD service-account UPN and password used for LDAP bind validation. `ADServiceAccount` and `ADServiceAccountCredential` are mutually exclusive and must never be supplied together. With `ADServiceAccountCredential`, the installer derives the UPN from its `UserName`. With `ADServiceAccount`, an interactive run requests the matching password separately. If neither value is provided interactively, the account prompt proposes `HybridDeviceTagger@<ActiveDirectoryForestDomain>` before the protected credential prompt. When local AD DNS discovery succeeds, the installer queries Active Directory, resolves the account's distinguished name, and stops if the account is absent, ambiguous, or unreadable. `ActiveDirectoryBindUserDistinguishedName` remains available as an explicit consistency check; when supplied, it must match the locally resolved account.

### Correcting the forest domain after deployment

```powershell
Import-Module ./HybridDeviceTagger.Management
Connect-HybridDeviceTagger -ApiBaseUri 'https://<function-app>.azurewebsites.net/api/management' `
    -ClientId '<management-api-client-id>' -SkipValidation
Get-HybridDeviceTaggerActiveDirectoryConfiguration
Set-HybridDeviceTaggerActiveDirectoryConfiguration -Domain 'contoso.com' -Confirm

# Only if the initial credential has not yet been initialized:
$password = Read-Host 'AD service-account password' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential -Password $password -Confirm
```

`GET` and `PUT /api/management/active-directory` expose the same configuration. A PUT body is `{ "domain": "contoso.com" }`; `If-Match` protects against concurrent changes. Changes require effective Function App Owner/Contributor access; management API application roles, including `PasswordRotationAdministrator`, are not sufficient because the domain selects the servers that receive the service-account password during binds. The configuration portal offers the same correction. Only DNS syntax is checked during a configuration change, so failed AD connectivity cannot prevent a repair. Subsequent forest connections on every instance read the private Blob override without a restart; already-running operations may still use the previous domain. The override survives installer reruns and takes precedence over `ActiveDirectory__Domain`. Update it through the management command rather than changing only the app setting. Existing rules, credential username, explicit account DN, and any manually configured `ActiveDirectory__Server` override are not changed automatically.

Network input is collected and validated incrementally. During interactive collection, the installer first proposes the target subscription as the virtual-network subscription; press Enter to use it or enter another subscription ID. A VNet in another subscription is supported when it belongs to the same Entra tenant and the deployment identity has the required read and subnet-join permissions there. The installer then asks for and validates the virtual-network resource group, VNet, FC1 integration subnet, and private-endpoint subnet in that order before validating the existing Active Directory service account and collecting Key Vault values. A missing interactively entered resource group, VNet, or subnet is reported and requested again. An interactively selected integration subnet that fails the FC1 requirements is also reported and requested again. A missing or unsuitable network resource supplied through `VirtualNetworkResourceGroupName`, `VirtualNetworkName`, `SubnetName`, or `PrivateEndpointSubnetName` terminates the run. The VNet and Function App must use the same Azure region; a mismatch terminates with a concise message identifying both regions and the available corrections. The integration subnet must use IPv4, meet the FC1 prefix requirements, be delegated exclusively to `Microsoft.App/environments`, contain no service endpoints or private endpoints, and differ from the private-endpoint subnet. The private-endpoint subnet must use IPv4 and must not be delegated.

The interactive resource-group prompt proposes `rg-HybridDeviceTagger`; press Enter to accept it or enter another resource group name.

The interactive location prompt proposes the Azure region `westeurope`; press Enter to accept it or enter another FC1-supported region.

The interactive Function App prompt proposes `func-devtagger-yyyyMMdd` using the current UTC date, for example `func-devtagger-20260904`; press Enter to accept it or enter another globally unique name.

Resource-name defaults follow the same workload-and-date naming scheme. For the default Function App name `func-devtagger-20260904`, the installer and Bicep template use:

- Storage account: `stdevtagger20260904` (without hyphens because Azure Storage permits only lowercase letters and numbers);
- Key Vault: `kv-devtagger-20260904`;
- Flex Consumption plan: `func-devtagger-20260904-plan`;
- Log Analytics workspace: `func-devtagger-20260904-law`;
- Application Insights: `func-devtagger-20260904-appi`;
- Storage private endpoints: `pe-stdevtagger20260904-blob` and `pe-stdevtagger20260904-queue`;
- Key Vault private endpoint (new vault only): `pe-kv-devtagger-20260904`.

`Install.ps1` proposes its own defaults instead: `func-hybriddevicetagger`, a Storage account name derived from it, and `kv-` plus the Function App name suffix.

The interactive Storage account prompt derives its default from the selected Function App name, adds the `st` prefix, removes unsupported characters, converts it to lowercase, and truncates the suffix when required by the 24-character Storage limit. The interactive Key Vault prompt similarly adds the `kv-` prefix to the Function App suffix and truncates it to the 24-character Key Vault limit. In existing-vault mode, its subscription and resource-group prompts default to the Function App deployment target. In new-vault mode, those values are fixed to that target. Existing VNet and subnet names are supplied separately and are not renamed.

The management API reads all domain cross-references from the forest configuration partition when rules are added. An OU-only `BaseDn`, such as `OU=Tier0,OU=Admin`, is resolved and subtree-tested in every forest domain; domains without that path are skipped, while a path absent from the entire forest fails validation. A full DN containing `DC=` is accepted only when its derived DNS domain exactly matches a `dnsRoot` advertised by the configured forest. Foreign, lookalike, and suffix-only domain matches are rejected before persistence and checked again before every tagging search, so rule data cannot redirect an authenticated LDAP bind outside the forest. Each rule must configure at least one extension attribute, Entra group Object ID, or administrative-unit Object ID. Group and AU targets are limited to 50 unique, nonempty GUIDs per type. The installer cannot prove the account's self-service `unicodePwd` change permission without actually rotating the password. Confirm `SELF / Change Password` separately before production installation.

Rules that target restricted management administrative units require the Function managed identity to receive the documented scoped custom role before tagging can modify those devices.

`-ActiveDirectoryTrustedCertificatePath` is required and accepts a DER or PEM public certificate. Supply the issuing root CA certificate in production, or the directly self-signed DC certificate for a lab. The installer uses it for its LDAPS preflight and writes the validated public certificate into the Function package as PEM without a private key. This packaged certificate is the initial LDAPS trust anchor. Function App Owners and Contributors can add, replace, or remove trust anchors at runtime in the configuration portal (`https://<function-app>.azurewebsites.net/api/portal`); the first change copies the packaged certificate into the private Blob `run-traces/active-directory-trust-anchors.json`, which then takes precedence. Private keys, expired certificates, and duplicates are rejected, and the last trust anchor cannot be removed. Management is restricted to Owners and Contributors because anyone able to inject a CA could impersonate a domain controller and capture the service-account password during the bind; those principals already control the Function code. Older or manually deployed Functions without a packaged certificate retain the operating system trust store fallback, but the installer no longer permits omitting the certificate. Runtime validation still requires Server Authentication EKU, a valid chain to that exact trust anchor, and a DNS name matching the LDAP endpoint. On Linux, `Novell.Directory.Ldap.NETStandard` performs custom trust and online revocation validation on the same TLS session used for bind, search, and password modification; Windows continues to validate the actual LDAP TLS session through `System.DirectoryServices.Protocols`.

For an update to an existing Function App, `-SkipActiveDirectoryDirectoryChecks` may be used when no matching AD credential is available interactively. This still requires `-ActiveDirectoryTrustedCertificatePath` and performs strict LDAPS endpoint validation, but skips the repeated forest and account queries. It is rejected for first-time installations.

The installer and Linux Flex Consumption Function prefer LDAPS on port 636 and securely fall back to LDAP upgraded with StartTLS on port 389. Both transports validate the discovered domain controller's DNS name and certificate before sending credentials. The installer resolves and validates the preexisting service account through LDAP rather than Active Directory Web Services, so ADWS-specific authentication policy does not block an otherwise valid runtime credential. Supply the issuing root CA certificate in production, or a directly self-signed domain-controller certificate only for a lab.

`-AllowLdapFallback` remains accepted for command-line compatibility but is no longer required: fallback is StartTLS-protected and automatic. Invalid credentials never trigger transport fallback, and the Function never uses an unprotected Basic LDAP bind.

## Configuration portal and activity log

The installer registers `https://<function-app>.azurewebsites.net/api/portal` as a single-page-application redirect URI of the management API app registration and grants tenant-wide consent for its own `access_as_user` scope. When the signed-in installer lacks permission for that grant, it only warns; an administrator must then grant consent in the Microsoft Entra admin center unless users may consent themselves. The portal is available only to effective Function App Owners and Contributors; management API application roles do not grant portal access. It shows the activity log, manages tagging rules and LDAP trust anchors, validates LDAP access with per-domain-controller TLS diagnostics, sets the initial password, changes the service account after a validated bind, and triggers a password change. German and English are selectable.

Activities are written to the existing Log Analytics workspace `<function-app>-law` (30-day retention, `AppTraces`, logger category `HybridDeviceTagger.Activity`). Application Insights local authentication is disabled; the managed identity receives `Monitoring Metrics Publisher` to ingest telemetry with Entra authentication and `Monitoring Reader` to query the activity log. Passwords and attribute values are never logged.

## What-if and installation

Deployment monitoring retries timed-out Azure CLI status queries with an explicit warning, without resubmitting the deployment. Each query is limited to 60 seconds or the remaining monitoring time, whichever is shorter; the overall monitoring limit is 30 minutes. Failed or canceled deployments and other query errors still terminate immediately. If monitoring expires without confirming a terminal state, check the deployment in Azure before rerunning: the infrastructure may already have completed, but Function code upload and credential initialization have not yet been reached.

Replace `-ValidateOnly` with `-WhatIf` to show the ARM changes without applying them. Remove both switches to install. Before the first Azure, Entra, Active Directory, Key Vault, or code change, the installer displays a summary of the selected mode, deployment target, resources, permissions, shared-resource references, and optional actions. It then uses standard PowerShell `ShouldProcess` confirmation for the complete plan. Use `-Confirm` to force the prompt. Use `-Force` for a fully noninteractive installation: it skips the central approval and disables every installer prompt, so all required values must be supplied. Missing information terminates the run instead of prompting. Validation and What-if display the same summary but do not request approval because they do not apply changes.

The central approval also covers `-RegisterProviders` and `-CreateResourceGroup`, ensuring those operations cannot mutate state before the summary and confirmation. LDAP fallback and restricted administrative-unit role assignment retain their additional purpose-specific safety prompts.

An empty interactive required value or an unsuitable interactively selected FC1 integration subnet is requested again rather than terminating the installer. By default, specifying a missing VNet resource group, VNet, integration subnet, or private-endpoint subnet, supplying an unsuitable integration subnet, or selecting a VNet in a different region prints only its actionable exception message. Run the installer with `-Verbose` when the full PowerShell error record, source location, and stack information are needed for those validation errors. Unexpected operational, Azure, Entra, Active Directory, and deployment failures remain terminating errors in both modes so automation can detect them reliably. The installer transcript is closed correctly in both modes.

## Updating tagging rules after installation

Import `HybridDeviceTagger.Management` and connect once to reuse the API URI and delegated Azure CLI token throughout the current PowerShell session. The production API URI and non-secret Entra audience are defaults; pass `ApiBaseUri` or `ClientId` to `Connect-HybridDeviceTagger` for another deployment.

```powershell
Import-Module ./HybridDeviceTagger.Management
Connect-HybridDeviceTagger

Get-HybridDeviceTaggerRule
```

Run this from the extracted package; from a repository checkout, import `./installationspackage/HybridDeviceTagger.Management`.

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
Invoke-HybridDeviceTagging -Wait
Invoke-HybridDeviceTaggerPasswordRotation -Wait -Confirm
```

The initialization command validates the supplied password against Active Directory and stores the current secret through the Function managed identity; it replaces only a missing secret or a deployment placeholder and never a real password (HTTP `409`). The password-rotation command has high confirmation impact and forces a real service-account password change. It bypasses only the configured age threshold; current credential verification, LDAPS, staging, validation, and secret promotion still apply.

Both actions run asynchronously. `POST actions/device-tagging` and `POST actions/password-rotation` return HTTP `202` with `{ invocationId, state: Queued, statusUri }` and the status URI in the `Location` header, and enqueue the run in the private Storage queue `device-tagging-requests` or `password-rotation-requests`. The queue-triggered functions `DeviceTaggingRequestFunction` and `PasswordRotationRequestFunction` execute the run and record `Queued`, `Running`, then `Succeeded` or `Failed` in the same status document as the timers. `host.json` sets `maxDequeueCount` to `1`, so a failed requested run is not retried automatically. A request for an operation that is already running receives HTTP `409`. Without `-Wait`, the cmdlets return the accepted request; with `-Wait`, they poll the status endpoint until that invocation finishes and return the final status. `-TimeoutSeconds` limits the wait (defaults: 1,800 seconds for tagging, 600 seconds for rotation). The active Entra identity needs the endpoint's application role but does not require Azure Resource Manager or Key Vault data access.

Device tagging and password rotation are each serialized across all Function instances and Flex Consumption scale groups by a renewable Blob lease on `run-traces/locks/*.lock` (60-second lease, renewed every 20 seconds with a 10-second renewal timeout). A watchdog cancels the run 45 seconds after the last successful renewal, before the lease can expire. A timer run that cannot acquire the lease is skipped with a warning. `MaximumInstanceCount` defaults to `1` per scale group.

Use `-RegisterProviders` to register missing providers during a real installation, and `-CreateResourceGroup` to create a missing target resource group. These switches do not make changes in validation or What-if mode. Use `-SkipCodeDeployment` when infrastructure and application deployment are separate pipeline stages.

For noninteractive Azure DevOps execution, build the package with `scripts/Build-InstallationPackage.ps1` (full Git history required), authenticate Azure CLI before invoking the engine from the extracted package, provide all parameters explicitly, construct `PSCredential` from a secret pipeline variable, and add `-Force`. Do not write the password or the credential object to logs. After a compactly reported failure, the engine exits with code `1` so the pipeline step fails.

## Existing-resource requirements

- The VNet and FC1 Function App must use the same Azure region.
- DNS must resolve every domain `dnsRoot` returned by the forest configuration partition from the Function integration subnet.
- Network routing and firewalls must allow LDAPS port 636 and, when fallback is enabled, LDAP port 389 to every affected forest domain.
- The subnet must be at least `/27`, have no underscore in its name, and be delegated only to `Microsoft.App/environments`.
- The subnet must not contain service endpoints or private endpoints.
- The separate private-endpoint subnet must permit private endpoints. The template creates the private endpoints `pe-<storage-account>-blob` and `pe-<storage-account>-queue` in this subnet.
- The installer reuses existing `privatelink.blob.core.windows.net` and `privatelink.queue.core.windows.net` zones linked to the selected VNet, or creates and links each missing zone in the Function App resource group. The Storage private endpoint zone groups create the A records before code deployment.
- When the VNet uses custom DNS servers, configure conditional forwarders for `privatelink.blob.core.windows.net` and `privatelink.queue.core.windows.net` to Azure DNS `168.63.129.16` from an Azure-hosted DNS server, or to a reachable Azure DNS Private Resolver inbound endpoint. The installer checks each Storage Blob and Queue hostname once per custom DNS server before uploading code; interactive failures require explicit user confirmation to continue without rechecking.
- A private Key Vault additionally requires a conditional forwarder for `privatelink.vaultcore.azure.net` to `168.63.129.16`. New-vault installations validate both private endpoints before code deployment.

### Repairing private-endpoint DNS during installation

If a custom VNet DNS server returns a public address, no A records, or a DNS query error, the installer reports that server and the observed result. It then prints the required fully qualified A records for **all** deployed Blob, Queue, and new Key Vault private endpoints, using their actual NIC addresses from Azure. Each record is grouped by its private zone, for example:

```dns
; Zone: privatelink.blob.core.windows.net
<storage-account>.privatelink.blob.core.windows.net. 300 IN A <blob-private-ip>
; Zone: privatelink.queue.core.windows.net
<storage-account>.privatelink.queue.core.windows.net. 300 IN A <queue-private-ip>
; Zone: privatelink.vaultcore.azure.net
<key-vault>.privatelink.vaultcore.azure.net. 300 IN A <vault-private-ip>
```

Use the concrete values printed by the installer, not these placeholders. Maintain the records on the DNS servers used by the Function VNet, reload the zone files, and clear stale caches as appropriate. Keep the public service CNAME chain pointing into those private zones; do not replace the whole public `blob.core.windows.net`, `queue.core.windows.net`, or `vault.azure.net` namespace. Conditional forwarding is an alternative to maintaining local A records and avoids stale addresses when private endpoints change.

Each endpoint name is queried once per configured DNS server. After configuring DNS, press **Enter** at the installer prompt to confirm that the required records or forwarding have been configured. The same installation then continues with role assignments and code deployment **without another DNS check**, even if the installation PC cannot reach the internal DNS servers. A warning explicitly states that the configuration is user-confirmed, not independently verified. Verify it from the Function VNet when local verification is impossible. Later deployment or runtime errors remain errors. Enter **A** to abort instead. Already deployed Azure resources remain in place.

Unattended runs (`-Force`, `CI`, or `TF_BUILD`) print the same guidance and terminate with an error without prompting; there is no implicit confirmation. Changing only the installation PC's hosts file cannot configure Function VNet DNS.
- The Key Vault must use RBAC, soft delete, and purge protection.
- A network-restricted Key Vault must have an approved private endpoint and DNS/routing reachable from the Function App subnet.
- The Function App managed identity receives `Storage Blob Data Contributor`, `Storage Queue Data Contributor`, `Key Vault Secrets Officer` on the two password secrets (or on a dedicated vault created with `-CreateKeyVault`), `Reader` on the Function App, and Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`.

The installer is idempotent: Bicep converges existing compatible resources, deterministic role-assignment names prevent duplicates, and the Graph role is checked before creation.

## Troubleshooting

### Installer transcript

Every `Install-HybridDeviceTagger.ps1` invocation, including those started by `Install.ps1`, replaces `HybridDeviceTagger-Install.log` in the current user's temporary directory. The PowerShell transcript captures the console workflow for validation, What-if, Active Directory preparation, and installation and is closed on success, failure, cancellation, or an early operational return.

```powershell
$installerLog = Join-Path ([System.IO.Path]::GetTempPath()) 'HybridDeviceTagger-Install.log'
code $installerLog
```

The transcript can contain user, device, resource, and directory names and must be treated as confidential. The script does not print passwords; do not write credentials to the console during manual troubleshooting.

### Latest-run diagnostic trace

Each device-tagging run, scheduled or requested, replaces `device-tagger-last-run.jsonl` in the private `run-traces` Blob container. Actions are appended immediately as JSON Lines with a UTC timestamp, sequence, invocation ID, phase, action, outcome, device and target IDs, and structured exception details. Extension-attribute values, passwords, and tokens are not written; attribute comparisons use SHA-256 fingerprints. The fixed latest-run file is intended for direct analysis in VS Code and remains useful when a run fails after only part of the workflow completed.

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