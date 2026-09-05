# HybridDeviceTagger Deployment Plan

Status: Validated

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

Management API release validation completed: `2026-09-05`.

- Azure extension and Azure CLI contexts both resolve to `kili@bloedgelaber.de`, tenant `d89a354d-2bce-44ed-b802-8f8600689247`, and subscription `23ca129f-1e1c-4a56-bc51-e3ec2c874a73`.
- Release build and the complete test suite passed with 64/64 tests; Bicep build/lint, PowerShell parsing/PSScriptAnalyzer, editor diagnostics, and `git diff --check` passed.
- Single-tenant Entra API registration `5e324803-5aed-4e5d-b49f-a4bfceddcda8` exposes `api://5e324803-5aed-4e5d-b49f-a4bfceddcda8/user_impersonation`, has an associated service principal, preauthorizes Azure CLI, and has no client secret.
- Easy Auth access is restricted to Entra object ID `43542018-427b-4143-b6c6-ca66d72e77bd` for `kili@bloedgelaber.de`.
- ARM validation passed. Detailed What-if reported 0 creates, 9 modifies, 0 deletes, 3 no-change, 1 unsupported, and 5 ignored resources. Existing Plan, Application Insights, Log Analytics, Storage, and private endpoint names are preserved without duplicate infrastructure.

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
- Temporary `Key Vault Secrets Officer` access for installation user `kili@bloedgelaber.de` was removed after verification.

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
