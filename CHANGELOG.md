# Changelog

All notable changes to HybridDeviceTagger are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the project format `<major>.<minor>.<yyyyMMdd>.<counter>`.

## [0.1.20261006.1] - 2026-10-06

### Added

- Add server-side Active Directory forest validation for rule base DNs before rule persistence.
- Add online CRL and OCSP validation for CA-issued LDAPS certificates in the installer and runtime, with fail-closed handling when revocation status cannot be confirmed.

### Changed

- Validate Linux LDAPS endpoints before native OpenLDAP binds while retaining explicit self-signed trust-anchor support.
- Expand management API, installer, operational, OpenAPI, and developer documentation for forest validation and certificate-revocation requirements.

### Security

- Reject rules that target a naming context outside the configured Active Directory forest.
- Reject revoked CA-issued LDAPS certificates and certificates whose revocation status is unavailable.

## [0.1.20261005.3] - 2026-10-05

### Added

- Add a complete PowerShell management-module reference covering installation, authorization, command usage, security, concurrency, errors, and implementation details.
- Add automatic, version-validated publishing of release modules from `main` to PowerShell Gallery by using the protected `HYBRIDDEVICETAGGER_PSMODUL` repository secret.
- Add comprehensive comment-based and inline implementation documentation for public and private PowerShell module functions.

### Changed

- Reorganize installation guidance into script-based, pipeline-based, and shared-configuration sections with secure unattended deployment examples.
- Expand the Zero Trust introduction and document post-installation module installation and updates through PowerShell Gallery.
- Enrich the PowerShell module manifest with Gallery tags, help, icon, and release-notes metadata.

## [0.1.20261005.2] - 2026-10-05

### Added

- Add Apache License 2.0 licensing, package metadata, SPDX source headers, and license inclusion in installation archives.
- Add runtime authorization that grants Function App `Owner` and `Contributor` principals rule and password administration through direct, transitive-group, or inherited Azure RBAC assignments.
- Add focused authorization tests and a Kili project logo with project, version, branch, platform, license, and support badges.

### Changed

- Replace the Microsoft contact addresses with `andreas.lucas@outlook.com` throughout the repository.
- Consolidate code and developer documentation in `DEVELOPER.md`, simplify its runtime architecture diagram, and remove the superseded standalone code-documentation file.
- Reformat `README.md` for GitHub with consistent headings, navigation, installation permissions, and complete rule-management examples.
- Disable Enterprise Application assignment enforcement so authenticated Function App Owners and Contributors can reach the API authorization layer while unauthorized principals continue to receive HTTP `403`.
- Migrate the active repository to `Kili69/HybridDeviceTagger` while preserving the complete source history, branches, and tags.

## [0.1.20261005.1] - 2026-10-05

### Changed

- Update the author contact address to `andreas.lucas@outlook.com`.
- Align this changelog with the Keep a Changelog 1.1.0 structure.
- Rename the root readme to `README.md` so GitHub recognizes it using the conventional file name.
- Rename `CODE_DOCUMENTATION.md` to `DEVELOPER.md` and update its repository reference.
- License the entire project under the Apache License 2.0 and publish the license in package metadata.
- Document the repository structure and the responsibilities of its main directories in the developer guide.

## [0.1.20260917.1] - 2026-09-17

### Added

- Add authenticated, one-time initialization of the Active Directory service-account credential through the Function management API and managed identity.
- Add an SDK-independent installation-package builder that nests the prebuilt Function application in the deployment package.
- Add PowerShell management support for initializing the AD credential and importing one or multiple tagging rules.

### Changed

- Allow the installer to create a private Key Vault or use an existing vault and prepare the AD service account with a separate provisioning credential.
- Remove direct Key Vault secret writes and Key Vault data-role requirements from the installation user.
- Expand installer, deployment, management API, OpenAPI, PowerShell module, and test coverage for secure credential initialization.
- Reorganize the German README and document all exported PowerShell management functions.

## [0.1.20260911.1] - 2026-09-11

### Added

- Add a public authentication metadata endpoint so the PowerShell module can discover the deployment-specific Entra token scope without a client-ID parameter.
- Add guided interactive tagging-rule creation while retaining object-based input for automation.
- Add project-specific VS Code build, launch, settings, and extension recommendations.
- Add focused tests for empty initial rule configuration, safe zero-rule execution, first-rule creation, and authentication metadata.

### Changed

- Separate infrastructure installation from business-rule configuration by deploying an empty rule set and removing rule and RMAU prompts from the installer.
- Make zero-rule tagging runs safe no-ops without Active Directory, Microsoft Graph, or membership-ledger activity while retaining final-rule deletion protection for configured environments.
- Expand the user, deployment, management API, monitoring, PowerShell command, and architecture documentation.

## [0.1.20260905.16] - 2026-09-05

### Added

- Document post-deployment management API role assignment, listing, and removal for users, security groups, and service principals through the Entra admin center and Microsoft Graph PowerShell.
- Document token renewal after role changes and the distinction between API application roles and Azure RBAC.

## [0.1.20260905.15] - 2026-09-05

### Added

- Add Blob-backed management of multiple Active Directory LDAPS trust certificates with package fallback, ETag concurrency, DER/PEM import, metadata-only listing, and final-anchor protection.
- Add authenticated REST endpoints and PowerShell cmdlets to list, add, and remove trusted certificates without restarting the Function App.
- Add the `HybridDeviceTagger.CertificateAdministrator` application role and role-specific installer assignments alongside Reader, rule, tagging, and password-rotation roles.

### Changed

- Authorize every management endpoint from Easy Auth role claims while retaining issuer, audience, authentication, and Enterprise Application assignment enforcement.
- Load the current certificate collection for every new LDAP connection and build a multi-anchor custom trust chain on Windows or an atomic PEM bundle for OpenLDAP.
- Expand management API, PowerShell module, deployment, OpenAPI, and operational documentation for role-based certificate administration.

### Fixed

- Hide runner methods behind explicit interfaces so Azure Functions discovers only the intended timer entry points.

### Removed

- Remove the superseded standalone management API and tagging-configuration wrapper scripts in favor of the reusable PowerShell module.

## [0.1.20260905.14] - 2026-09-05

### Changed

- Rename the single-rule upsert cmdlet from `Set-HybridDeviceTaggerRule` to `Add-HybridDeviceTaggerRule` while retaining `ShouldProcess` support.

## [0.1.20260905.13] - 2026-09-05

### Changed

- Return structured `HybridDeviceTagger.TraceEvent` objects from `Get-HybridDeviceTaggerTrace` while displaying each event by default as one readable `<time>,<severity>,<phase>,<status>,<action>,<details>` line.
- Reuse the module's trace-object formatting in `Show-HybridDeviceTaggerApi.ps1` instead of converting trace events to strings in the wrapper.

## [0.1.20260905.12] - 2026-09-05

### Added

- Add a reusable stateful PowerShell management module for rules, statuses, traces, immediate tagging, and forced service-account password rotation.
- Add authenticated REST actions for immediate device tagging and forced password rotation with conflict protection against overlapping runs.

### Changed

- Reuse the timer orchestration, status persistence, trace handling, and security checks for manually invoked operations.
- Refactor the existing read and configuration scripts to use the shared direct REST API connection without Azure Resource Manager discovery.

## [0.1.20260905.11] - 2026-09-05

### Changed

- Disable automatic GitHub Actions triggers because Enterprise policy blocks GitHub-hosted runners; retain manual workflow dispatch for future runner availability.

## [0.1.20260905.10] - 2026-09-05

### Changed

- Add the Active Directory machine name to trace details alongside the device ID for devices discovered in the current scope.
- Rename the read client's latest-run operation to `ShowLastRunStatus`, retaining `ShowStatus` as a compatibility alias.
- Internalize the production Entra API client ID while continuing to use it as the required OAuth token audience.

## [0.1.20260905.9] - 2026-09-05

### Changed

- Format each `ShowTrace` event as `<time>,<severity>,<phase>,<status>,<action>,<details>` on one logical line, with an ISO-8601 timestamp and compact JSON details.

## [0.1.20260905.8] - 2026-09-05

### Changed

- Emit each `ShowTrace` event as one compact JSON line for readable console output and direct JSONL processing.

## [0.1.20260905.7] - 2026-09-05

### Changed

- Default the read client to the production management API URL and non-secret Entra token audience so users with an existing Azure CLI sign-in only need `ShowRules`, `ShowStatus`, or `ShowTrace`.

## [0.1.20260905.6] - 2026-09-05

### Changed

- Make `Show-HybridDeviceTaggerApi.ps1` a direct REST client that requires only the management API base URI and Entra API client ID, without Azure subscription, resource group, Function App, or ARM discovery parameters.

## [0.1.20260905.5] - 2026-09-05

### Added

- Add `Show-HybridDeviceTaggerApi.ps1` for displaying current rules, the latest device-tagging status, and parsed latest-run trace events through the Entra-authenticated REST API.

### Changed

- Update operational documentation to describe Blob-backed rule changes without Function App restarts and include examples for all read-only API client modes.

## [0.1.20260905.4] - 2026-09-05

### Fixed

- Serialize management API operation states as readable `Running`, `Succeeded`, and `Failed` values while continuing to read status Blobs written with numeric enum values.

## [0.1.20260905.3] - 2026-09-05

### Fixed

- Read deployed Easy Auth settings through the supported ARM `GET` resource endpoint and support nested Azure CLI Function App properties in the REST-based tagging configuration script.

## [0.1.20260905.2] - 2026-09-05

### Added

- Add an Entra Easy Auth protected REST API for reading and changing tagging rules, reading latest tagging and password-rotation status, and downloading the latest JSONL trace.
- Persist dynamic tagging rules and latest operation statuses in private Blob Storage with startup app-setting fallback and optimistic ETag concurrency.
- Add an OpenAPI 3.0 contract and focused tests for configuration mutations, authentication headers, status responses, and streamed trace downloads.

### Changed

- Load one current tagging-rule snapshot for each device-tagging invocation without requiring a Function App restart.
- Update the tagging-configuration PowerShell workflow to use the management API with an Entra bearer token and `If-Match` instead of modifying app settings.
- Configure single-tenant Easy Auth with explicit allowed Entra principal object IDs during deployment.

## [0.1.20260905.1] - 2026-09-05

### Fixed

- Resolve the managed-identity storage account setting through the normalized .NET configuration key so tagging runs and latest-run trace writes can access Azure Blob Storage.

## [0.1.20260904.3] - 2026-09-04

### Added

- Remove managed extension attributes and owned group, AU, and RMAU memberships when a computer leaves all configured target OUs.
- Preserve manually changed extension attributes and pre-existing memberships during OU-exit cleanup.
- Add an immediately persisted JSONL trace of the latest device-tagging run in private Blob Storage for detailed VS Code and Copilot analysis.
- Add a latest-run PowerShell transcript for the installer in the current user's temporary directory.

### Changed

- Require the central product version and this changelog to be updated before every commit.
- Add a repository pre-commit check for version and history consistency.
- Add standardized project-version, author, and sample-disclaimer headers to production sources.
- Add complete comment-based help to `Install-HybridDeviceTagger.ps1`.
- Make the installer account-name assertion optional and collect interactive inputs in a deterministic order, starting with an omitted target subscription and the active Azure CLI subscription as its default.
- Validate and confirm the selected Azure subscription before collecting or validating dependent installation inputs.
- Default the interactive target resource-group prompt to `rg-HybridDeviceTagger`.
- Default the interactive Azure location prompt to `westeurope`.
- Default the interactive Function App name to `func-devtagger-yyyyMMdd` using the current UTC date.
- Default the interactive existing Key Vault name to `kv-devtagger-yyyyMMdd`, derived from the selected Function App name.
- Default the interactive existing Key Vault resource group to the selected Function App resource group.
- Propose the logged-on Active Directory user's forest DNS name during interactive installation while keeping the value explicit on workgroup systems.
- Use the Active Directory forest DNS name for domain-controller discovery when no explicit Active Directory server is supplied.
- Label the interactive Active Directory bind-username prompt as `Service Account`.
- Default the interactive service-account UPN to `HybridDeviceTagger@<AD-forest>` and assign a fixed functional description when creating the account.
- Replace the `ExpectedAzureAccount` installer parameter with `ADServiceAccount` for the Active Directory bind UPN.
- Resolve the distinguished name of an existing AD service account from its UPN instead of prompting for it.
- Replace the interactive tagging-rules JSON input with guided first-rule creation and practical defaults.
- Prompt interactively for the AD service-account password when bind validation is enabled and `ActiveDirectoryCredential` was omitted.
- Align app-owned Azure resource names with the Function App's `devtagger-yyyyMMdd` scheme and default the Storage account to `stdevtaggeryyyyMMdd`.
- Show a complete installation summary and require one standard PowerShell confirmation before any Azure, Entra, Active Directory, Key Vault, or code change.
- Repeat empty interactive required-value prompts, and print only the actionable message for missing VNet resources by default while retaining full PowerShell diagnostics with `-Verbose` and terminating behavior for unexpected operational failures.
- Prompt for and validate the virtual-network resource group before collecting the VNet and subnet names, then validate both subnets immediately.
- Allow interactive selection of a separate VNet subscription and repeat missing interactive VNet resource-group, VNet, and subnet prompts while keeping missing parameter-selected resources terminating.
- Repeat the FC1 integration-subnet prompt when an interactive selection is unsuitable and clearly require exclusive `Microsoft.App/environments` delegation.
- Report a VNet and Function App region mismatch with a concise actionable message by default and full diagnostics with `-Verbose`.

## [0.1.20260904.1] - 2026-09-04

### Added

- Add rule-based Entra group and administrative-unit membership management.
- Add a private Blob ownership ledger so only memberships created by HybridDeviceTagger are removed.
- Add editable post-install tagging configuration and explicit restricted administrative-unit approval.
- Add forest-wide OU resolution, LDAP connection hardening, tests, and deployment documentation.

### Changed

- Extend Microsoft Graph permissions for group and administrative-unit membership reconciliation.
- Deploy and verify the membership release in the production Function App.

## [0.1.20260903.2] - 2026-09-03

### Added

- Add safe Active Directory service-account password rotation and recovery.
- Add LDAPS trust validation and signed, sealed LDAP fallback support.

## [0.1.20260903.1] - 2026-09-03

### Added

- Initial .NET 8 isolated Azure Functions implementation for Active Directory to Entra device tagging.
