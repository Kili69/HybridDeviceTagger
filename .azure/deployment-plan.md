# HybridDeviceTagger Deployment Plan

Status: Ready for Validation

## Approved Change: Optional Key Vault Creation

- Interactive installation asks whether to reuse an existing Key Vault or create a new one.
- Existing-vault mode requests and validates the vault name, resource group, and subscription.
- New-vault mode creates the vault in the Function App resource group and subscription, with Azure RBAC, soft delete, purge protection, disabled public access, a private endpoint in the configured private-endpoint subnet, and private DNS integration.
- Noninteractive installation uses an explicit switch to select new-vault mode; existing-vault mode remains the default.
- The installer writes and validates the current AD credential only after a newly selected vault has been deployed and the installation identity has access.
- Bicep, PowerShell, documentation, and generated installation-package contents will be validated without deploying resources.

### Validation Steps

- [x] PowerShell parser and PSScriptAnalyzer validation
- [x] Bicep compilation and linter diagnostics
- [x] Conditional Key Vault, private endpoint, DNS, and RBAC template review
- [x] .NET regression tests
- [x] Installation-package build and content verification
- [x] Documentation and whitespace validation
- [ ] ARM validation and What-if with installation-specific credentials (not run unless explicitly requested)

## 1. Workload

- Existing .NET 8 Azure Functions v4 isolated-worker application.
- Timer-triggered forest-wide Active Directory device tagging and AD service-account password rotation.
- Deployment method: existing `Deployment/Install-HybridDeviceTagger.ps1` and `Deployment/main.bicep`.

## 2. Azure Context

- Account: `kili@bloedgelaber.de`
- Tenant: `d89a354d-2bce-44ed-b802-8f8600689247`
- Subscription: `ME-MngEnvMCAP458299-anluca-1` (`23ca129f-1e1c-4a56-bc51-e3ec2c874a73`)
- Region: `westus2`
- Target resource group: `RG-HybridDeviceTagger`

## 3. Existing Dependencies

- VNet: `OnPrem-Network/BG-OnPrem`
- FC1 integration subnet: `snet-hybrid-device-tagger` (`10.0.4.0/24`)
- Dedicated Key Vault: `RG-HybridDeviceTagger/kv-hybriddevicetagger`
- Key Vault private endpoint: `pe-kv-hybriddevicetagger` in `AzServices` (`10.0.3.6`)
- Active Directory forest: `bloedgelaber.de`
- Domain controller: `bgdc200.bloedgelaber.de`

## 4. Planned Resources

- Function App: `func-hybriddevicetagger`
- Storage account: `sahdtagger`
- Flex Consumption plan and supporting monitoring resources defined by `Deployment/main.bicep`.
- Managed identity RBAC for Key Vault and deployment storage.
- Microsoft Graph application roles `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`.
- Management API application roles `Reader`, `RuleAdministrator`, `TaggingOperator`, `PasswordRotationAdministrator`, and `CertificateAdministrator` with assignment required.
- Private Blob-backed management of multiple public Active Directory LDAPS trust certificates, with the packaged certificate as the bootstrap fallback.

## 5. Security And Data Handling

- Managed identity for Azure and Graph access.
- AD credentials are entered through local PowerShell credential dialogs and are not passed to ARM.
- Key Vault uses RBAC, soft delete, purge protection, disabled public access, and private networking.
- `kili@bloedgelaber.de` temporarily has `Key Vault Secrets Officer` on the dedicated vault for bootstrap; remove this assignment after deployment verification.
- AD account creation or password reset requires separate explicit confirmation. The installer prefers LDAPS and uses the Active Directory PowerShell module over Active Directory Web Services when LDAPS is unavailable and LDAP fallback was approved.
- Function read and bind fallback uses Negotiate authentication with LDAP signing and sealing on port 389. Automatic password rotation remains deferred until LDAPS is available.
- `pwdLastSet=-1` prevents password change at first logon.

## 6. Execution Gates

- [x] Initialize and verify `hybrid-device-tagger-ad-password-current`.
- [x] Run complete installer `-ValidateOnly` successfully.
- [x] All validation checks pass.
	- [x] 1. Core Validation (CLI, auth, build, validate, what-if)
	- [x] 2. Linting (optional)
	- [x] 3. Azure Policy Validation
- [x] Run ARM What-if and review changes.
- [x] Obtain final deployment approval.
- [x] Run live installation.
- [x] Verify Function App, managed identity, RBAC, settings, package deployment, and health.

## 7. Validation Proof

Optional Key Vault creation local validation completed: `2026-09-17`.

- `Deployment/main.bicep` compiled through the Bicep MCP compiler with no diagnostics after separating existing- and new-vault role-assignment dependencies.
- `Deployment/Install-HybridDeviceTagger.ps1` and `scripts/New-InstallationPackage.ps1` passed PowerShell parsing and PSScriptAnalyzer with zero warning- or error-level findings.
- The complete .NET test suite passed with 89/89 tests.
- Static review verified Function-mediated Key Vault initialization, private endpoint creation, linked private DNS zone reuse, secret initialization outside ARM, and idempotent continuation after partial deployment.
- `artifacts/HybridDeviceTagger-0.1.20260905.16.zip` was rebuilt and contains the updated installer, both Bicep files, documentation, and a complete inner Function package.
- Editor diagnostics and `git diff --check` completed without findings.
- ARM validation and What-if were not run because this change request did not authorize validation against a concrete Azure deployment context.

Certificate-management release validation completed: `2026-09-05`.

- The complete .NET Release suite passed with 89/89 tests, including eight Blob trust-store tests and focused API, authorization, multi-root Windows validation, and Linux PEM-bundle coverage.
- `Deployment/main.bicep` compiled successfully through the Bicep MCP compiler and Azure CLI with no diagnostics. The generic parameterless ARM validation helper is not applicable because this repository intentionally builds required deployment parameters in `Deployment/Install-HybridDeviceTagger.ps1`; the existing live infrastructure and installer validation remain the deployment authority.
- Production PowerShell files passed parsing and PSScriptAnalyzer with no warnings or errors. The module exports 13 commands, including all three documented trusted-certificate commands.
- The OpenAPI 3.0 document is valid. Redocly reported nine pre-existing documentation-quality warnings outside the new trusted-certificate paths and schemas.
- Static review confirmed the stable `CertificateAdministrator` role ID, preservation of unrelated app roles, role-specific assignment with bootstrap fallback, and app-role assignment before `appRoleAssignmentRequired` enforcement.
- Effective policy assignments for `RG-HybridDeviceTagger` were retrieved under `kili@bloedgelaber.de` and reviewed. The update does not weaken the current FC1, private-network, managed-identity, Key Vault, Storage, Easy Auth, or Application Insights controls.
- `git diff --check` completed without whitespace errors. No production certificate mutation or password rotation was invoked during validation.
- `HybridDeviceTagger.CertificateAdministrator` is live with stable role ID `659858ff-b645-4b06-90ff-e79314628687`; the operator has all five role assignments and `appRoleAssignmentRequired` remains enabled.
- The updated Function package was deployed after verifying LDAPS thumbprint `E9C632C3ADE1CA74EBBBAF9CE114AB47FD3C619D`. Azure reports 14 Functions, including all three trusted-certificate operations.
- Read-only production verification returned HTTP `401` anonymously and HTTP `200` with a five-role token. It listed one metadata-only certificate from source `package`, exposed no certificate body, and Application Insights contained no exceptions or severity 3+ traces after deployment.

Management API role-authorization validation completed: `2026-09-05`.

- Azure extension and MCP contexts resolve to `kili@bloedgelaber.de`, tenant `d89a354d-2bce-44ed-b802-8f8600689247`, and subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73`.
- The complete .NET test suite passed with 76/76 tests. Focused authorization tests passed with 16/16 tests and verify HTTP `401`, HTTP `403`, short and URI role claims, and privileged-action denial. Two reflection tests prevent ambiguous public timer entry points.
- `Deployment/main.bicep` compiled successfully with no diagnostics. Editor diagnostics and `git diff --check` are clean.
- `Deployment/Install-HybridDeviceTagger.ps1` passed PowerShell parsing and PSScriptAnalyzer with no findings. Static review confirmed that app-role assignments are created before `appRoleAssignmentRequired` is enabled.
- Effective Azure Policy assignments for `RG-HybridDeviceTagger` were retrieved and reviewed. This update does not weaken the existing network, identity, storage, or Key Vault controls.
- Production verification will not invoke the forced password-rotation endpoint because it intentionally changes the Active Directory service-account password.
- The management API app and Enterprise Application each expose all four roles. `kili@bloedgelaber.de` has all four migration assignments, `appRoleAssignmentRequired` is enabled, and the legacy Easy Auth allowed-identity list is empty.
- The role-aware Function package was deployed successfully as deployment `042a2705-50e1-47ab-b9c5-d2c5cfaa0e8b` after validating LDAPS trust-anchor thumbprint `E9C632C3ADE1CA74EBBBAF9CE114AB47FD3C619D`.
- Anonymous API access returns HTTP `401`. A previously cached delegated token without a role claim returns the expected HTTP `403` and sanitized error. Positive role-bearing token verification remains pending until Azure CLI renews its token cache.
- Deployment telemetry exposed ambiguous `RunAsync` overloads on both timer classes. Their runner methods now use explicit interface implementations, the regression tests pass, and Application Insights contains no exceptions after the final deployment completed at `2026-09-05T15:29:55Z`.

Certificate-management deployment is pending. Before deploying the updated Function package, add and assign `HybridDeviceTagger.CertificateAdministrator`; retain the approved packaged LDAPS certificate as the fallback. Production verification must list certificate metadata only and must not add or remove a trust anchor.

Management operations release validation completed: `2026-09-05`.

- Azure MCP and Azure CLI resolve to `kili@bloedgelaber.de`, tenant `d89a354d-2bce-44ed-b802-8f8600689247`, and the explicitly reconfirmed subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73`.
- Release publish completed successfully and generated all 11 expected timer and HTTP Functions, including `RunDeviceTagging` and `RunPasswordRotation`.
- The complete test suite passed with 70/70 tests, including immediate API operations, concurrent-run conflict handling, and forced rotation of a recent password.
- Every production PowerShell file passed parser and PSScriptAnalyzer validation; the new module manifest imports and exports the intended 10 public commands.
- Commit metadata validation, editor diagnostics, OpenAPI diagnostics, and `git diff --check` passed for version `0.1.20260905.12`.
- Azure Policy assignments were reviewed through Azure MCP, and the unchanged Bicep infrastructure compiled successfully. Infrastructure and RBAC definitions are unchanged; existing scoped Storage, Key Vault, and Microsoft Graph roles remain sufficient.
- Production verification will not invoke the forced password-rotation action because it intentionally changes the Active Directory service-account password.
- Code package deployment to `func-hybriddevicetagger` completed successfully with the approved self-signed LDAPS trust anchor `E9C632C3ADE1CA74EBBBAF9CE114AB47FD3C619D`.
- Azure registered all 11 Functions. Both new action routes rejected anonymous POST requests with HTTP `401`.
- The new PowerShell module connected to the production API, read two rules and both statuses, and invoked immediate tagging as correlation ID `83f80ca2-36b3-4de1-9e46-5ae30dbf4505`.
- The immediate run succeeded with six discovered, zero updated, and six unchanged devices; its status and 38-event trace were persisted and retrieved through the API.
- Live RBAC verification confirmed resource-scoped Storage Blob Data Owner and Key Vault Secrets Officer assignments plus Microsoft Graph `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All` for managed identity `19346b5c-09d0-4ac5-a4fd-03b8253f7e01`.

Machine-name trace release validation completed: `2026-09-05`.

- Azure CLI and Azure MCP resolve to `kili@bloedgelaber.de`, tenant `d89a354d-2bce-44ed-b802-8f8600689247`, and confirmed subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73`.
- Release build completed without warnings; the complete test suite passed with 65/65 tests, including focused assertions for `machineName` in device and membership trace details.
- `ShowLastRunStatus` and its compatibility alias `ShowStatus` both returned the production `device-tagging` run status through the Easy Auth protected REST API.
- The read client exposes no `ManagementApiClientId` parameter; the fixed non-secret API audience remains internal and is used only for Azure CLI token acquisition.
- Bicep build and lint, PowerShell parsing and PSScriptAnalyzer, commit metadata validation, editor diagnostics, and `git diff --check` passed.
- Infrastructure and RBAC definitions are unchanged. Existing resource-scoped Storage and Key Vault roles and the three documented Microsoft Graph application roles remain sufficient.

Management API release validation completed: `2026-09-05`.

- Azure extension and Azure CLI contexts both resolve to `kili@bloedgelaber.de`, tenant `d89a354d-2bce-44ed-b802-8f8600689247`, and subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73`.
- Release build and the complete test suite passed with 64/64 tests; Bicep build/lint, PowerShell parsing/PSScriptAnalyzer, editor diagnostics, and `git diff --check` passed.
- Single-tenant Entra API registration `5e324803-5aed-4e5d-b49f-a4bfceddcda8` exposes `api://5e324803-5aed-4e5d-b49f-a4bfceddcda8/user_impersonation`, has an associated service principal, preauthorizes Azure CLI, and has no client secret.
- Easy Auth access is restricted to Entra object ID `43542018-427b-4143-b6c6-ca66d72e77bd` for `kili@bloedgelaber.de`.
- ARM validation passed. Detailed What-if reported 0 creates, 9 modifies, 0 deletes, 3 no-change, 1 unsupported, and 5 ignored resources. Existing Plan, Application Insights, Log Analytics, Storage, and private endpoint names are preserved without duplicate infrastructure.
- Post-deployment checks discovered all nine functions, rejected anonymous API access with HTTP 401, and returned both production rules and the latest JSONL trace with an Entra token. Rule2 and its app-setting fallback were restored to `DC=bloedgelaber,DC=de` after stale historical deployment parameters briefly reintroduced the old spelling.

Membership release validation completed: `2026-09-04`.

- Azure CLI account, tenant, and subscription match the approved production context.
- Release build passed with 0 warnings and 0 errors; focused test runs passed 39/39 and 18/18.
- Both deployment scripts passed PowerShell parsing and PSScriptAnalyzer; Bicep build and lint passed.
- Effective Azure Policy assignments were reviewed for the target resource group.
- The installer `-ValidateOnly` run passed against the current live rules using the credential-free existing-installation path; strict LDAPS validation matched thumbprint `E9C632C3ADE1CA74EBBBAF9CE114AB47FD3C619D`.
- ARM What-if reported 1 create, 6 modifies, 0 deletes, 3 no-change, 1 unsupported, and 5 ignored resources. The sole create is the private `membership-ledger` Blob container. Storage public network access remains `Disabled`.
- Static role review confirmed resource-scoped Storage and Key Vault roles, the three required Microsoft Graph app roles, and restricted administrative-unit roles scoped only to configured RMAUs. The current live rules contain no administrative-unit targets.
- Live membership release installation completed successfully on `2026-09-04`; the Function package was published after all Graph role assignments succeeded.
- Managed identity `19346b5c-09d0-4ac5-a4fd-03b8253f7e01` has `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All`.
- Function App `func-hybriddevicetagger` is running on the `FC1` Flex Consumption plan. Azure discovered `DeviceTaggerFunction` and `PasswordRotationFunction`.
- Storage public network access remains `Disabled`, Blob public access remains disabled, and the private `membership-ledger` container exists with no public access.
- The two production rules were preserved unchanged. A transient worker start during the app-settings update was followed by a clean host restart and host-lock acquisition at `2026-09-04T13:00:48Z`; no later exceptions or error-level traces were observed.

- Validation completed: `2026-09-03`.
- AD password reset completed through Active Directory Web Services; the randomized password was synchronized directly to Key Vault without disclosure.
- Current secret metadata verified: enabled, created `2026-09-03T20:59:47Z`, expires `2026-10-08T20:59:46Z`.
- Signed and sealed LDAP Negotiate bind and RootDSE query succeeded against `bgdc200.bloedgelaber.de:389`.
- Forest identity, configured OU, device read access, and service-account object validation succeeded.
- Installer `-ValidateOnly` and ARM template validation completed successfully without resource changes.
- Bicep build and lint completed successfully; the effective Azure Policy assignments were reviewed and ARM validation reported no policy violations.
- ARM What-if completed with 8 creates, 0 modifies, and 0 deletes. Existing Key Vault and private-network resources are ignored. The only unsupported prediction is the expected Key Vault role assignment whose managed-identity principal ID is deployment-time data.
- Release build completed with 0 warnings and 0 errors; 14 tests passed.
- Live ARM deployment completed successfully. Azure Policy forced Storage public network access to `Disabled`, so the approved Blob private endpoint `pe-sahdtagger-blob` was added in `BG-OnPrem/AzServices` with private IP `10.0.3.7`.
- AD DNS resolves `sahdtagger.blob.core.windows.net` through `sahdtagger.privatelink.blob.core.windows.net` to `10.0.3.7`; the Function package deployment then completed successfully.
- Function App `func-hybriddevicetagger` is running on FC1 with the .NET 8 isolated runtime and the expected VNet integration. Azure discovered `DeviceTaggerFunction` and `PasswordRotationFunction`.
- Managed identity `19346b5c-09d0-4ac5-a4fd-03b8253f7e01` has `Storage Blob Data Owner`, `Key Vault Secrets Officer`, and Microsoft Graph `Device.ReadWrite.All` at the intended scopes.
- AD credential initialization is performed by the Function managed identity through the Entra-protected management API; the installation user has no Key Vault data role.

Commands and checks executed:

- `dotnet build .\HybridDeviceTagger.sln --configuration Release --no-restore` — passed with 0 warnings and 0 errors.
- VS Code test runner for all three test files — 14 passed, 0 failed.
- `Invoke-ScriptAnalyzer -Path .\Deployment\Install-HybridDeviceTagger.ps1 -Severity Warning,Error` — no findings.
- `az bicep build --file .\Deployment\main.bicep --stdout` — passed.
- `az bicep lint --file .\Deployment\main.bicep` — no findings.
- `Install-HybridDeviceTagger.ps1 ... -ValidateOnly` with the credential sourced in memory from Key Vault — passed; no resources changed.
- `Install-HybridDeviceTagger.ps1 ... -WhatIf` with the credential sourced in memory from Key Vault — passed; 8 creates, 0 modifies, 0 deletes.
- Azure Policy assignment query for subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73` — effective assignments reviewed; ARM validation reported no violations.
- Live deployment `hybrid-device-tagger-20260903213609` — succeeded after adding the policy-required Storage private endpoint.
- Function package deployment `44e7f7a3-9735-4b87-9e68-716605794261` — succeeded.
- Post-deployment `dotnet test .\HybridDeviceTagger.sln --configuration Release --no-restore` — 14 passed, 0 failed.

## 8. Rollback

- Stop or disable both timer functions before remediation if post-deployment verification fails.
- Remove newly created app-owned resources from `RG-HybridDeviceTagger` only after explicit approval.
- Do not remove the shared VNet, subnets, dedicated Key Vault, private endpoint, DNS records, or AD account automatically.

## 9. Role Assignment Verification

- Status: Verified.
- Identity checked: system-assigned managed identity of `func-hybriddevicetagger`.
- `Storage Blob Data Owner` is scoped to `sahdtagger` for managed-identity deployment package access.
- `Key Vault Secrets Officer` is scoped to `kv-hybriddevicetagger` for current/next secret read, stage, and promotion operations.
- Microsoft Graph application roles `Device.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `AdministrativeUnit.ReadWrite.All` are assigned to the same principal for device attributes, group membership, and administrative-unit membership.
- No generic subscription- or resource-group-scoped Reader, Contributor, or Owner role is assigned to the Function identity.
