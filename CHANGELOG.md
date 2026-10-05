# Changelog

All notable changes to HybridDeviceTagger are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the project format `<major>.<minor>.<yyyyMMdd>.<counter>`.

## [Unreleased]

## [0.1.20261005.1] - 2026-10-05

### Changed

- Update the author contact address to `andreas.lucas@outlook.com`.
- Align this changelog with the Keep a Changelog 1.1.0 structure.
- Rename the root readme to `README.md` so GitHub recognizes it using the conventional file name.
- Rename `CODE_DOCUMENTATION.md` to `DEVELOPER.md` and update its repository reference.
- License the entire project under the Apache License 2.0 and publish the license in package metadata.
- Document the repository structure and the responsibilities of its main directories in the developer guide.

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

[unreleased]: https://github.com/Kili69/HybridDeviceTagger/compare/9188b02ca008e5993910fd782beedda9f6c40a3b...HEAD
[0.1.20261005.1]: https://github.com/Kili69/HybridDeviceTagger/compare/9188b02ca008e5993910fd782beedda9f6c40a3b...HEAD
[0.1.20260911.1]: https://github.com/Kili69/HybridDeviceTagger/compare/96cd5c7a43a6140e45ffda3a907632b58ab3fe1f...9188b02ca008e5993910fd782beedda9f6c40a3b
[0.1.20260905.16]: https://github.com/Kili69/HybridDeviceTagger/compare/7836d5a34839bf6832d5c7ae0a83c369c9630df2...96cd5c7a43a6140e45ffda3a907632b58ab3fe1f
[0.1.20260905.15]: https://github.com/Kili69/HybridDeviceTagger/compare/485a6d9f35b8c0e6b4724867b054726b0f53cab6...7836d5a34839bf6832d5c7ae0a83c369c9630df2
[0.1.20260905.14]: https://github.com/Kili69/HybridDeviceTagger/compare/3c3534971ac08d7044f1149321df72d626c61405...485a6d9f35b8c0e6b4724867b054726b0f53cab6
[0.1.20260905.13]: https://github.com/Kili69/HybridDeviceTagger/compare/e0bd235fe2c5ae0904583a20faed5a2cf565ea84...3c3534971ac08d7044f1149321df72d626c61405
[0.1.20260905.12]: https://github.com/Kili69/HybridDeviceTagger/compare/8b0eb7f1461837f614c48b85e04cefa0b3f4ed90...e0bd235fe2c5ae0904583a20faed5a2cf565ea84
[0.1.20260905.11]: https://github.com/Kili69/HybridDeviceTagger/compare/872e1bdc8321a9202f25c8275ee879b4f21c4a13...8b0eb7f1461837f614c48b85e04cefa0b3f4ed90
[0.1.20260905.10]: https://github.com/Kili69/HybridDeviceTagger/compare/12297b58d3da428e0a7dbe17cff5bf510dc723fe...872e1bdc8321a9202f25c8275ee879b4f21c4a13
[0.1.20260905.9]: https://github.com/Kili69/HybridDeviceTagger/compare/88ab584ea7517c7e2215554ccc04ceb5bb3286c8...12297b58d3da428e0a7dbe17cff5bf510dc723fe
[0.1.20260905.8]: https://github.com/Kili69/HybridDeviceTagger/compare/84ed6b65ae593b757f12dc5df565d04b096acf9f...88ab584ea7517c7e2215554ccc04ceb5bb3286c8
[0.1.20260905.7]: https://github.com/Kili69/HybridDeviceTagger/compare/ce0de3fc74916f23a85e913948fe2f502ca06391...84ed6b65ae593b757f12dc5df565d04b096acf9f
[0.1.20260905.6]: https://github.com/Kili69/HybridDeviceTagger/compare/b876b27a88438e8681961cbd922470c38f08b801...ce0de3fc74916f23a85e913948fe2f502ca06391
[0.1.20260905.5]: https://github.com/Kili69/HybridDeviceTagger/compare/fbc3166e7c5bb18269ffd0350c05ef48c84edb0b...b876b27a88438e8681961cbd922470c38f08b801
[0.1.20260905.4]: https://github.com/Kili69/HybridDeviceTagger/compare/237b9602ba8a0235256776eb80f2e5da829cd6a7...fbc3166e7c5bb18269ffd0350c05ef48c84edb0b
[0.1.20260905.3]: https://github.com/Kili69/HybridDeviceTagger/compare/866482e87981a187e3cce39e0a0dc25625cfa086...237b9602ba8a0235256776eb80f2e5da829cd6a7
[0.1.20260905.2]: https://github.com/Kili69/HybridDeviceTagger/compare/285e462332d713e40e9291c59c6fbfd4c46a5efd...866482e87981a187e3cce39e0a0dc25625cfa086
[0.1.20260905.1]: https://github.com/Kili69/HybridDeviceTagger/compare/270e2d0fe518accc54be9b1a490f214e60d2fcf0...285e462332d713e40e9291c59c6fbfd4c46a5efd
[0.1.20260904.3]: https://github.com/Kili69/HybridDeviceTagger/compare/4587d3a...270e2d0fe518accc54be9b1a490f214e60d2fcf0
[0.1.20260904.1]: https://github.com/Kili69/HybridDeviceTagger/compare/968e6fe2a9efd81b61db5840d4f83995170175fe...4587d3a
[0.1.20260903.2]: https://github.com/Kili69/HybridDeviceTagger/compare/a6f224e...968e6fe2a9efd81b61db5840d4f83995170175fe
[0.1.20260903.1]: https://github.com/Kili69/HybridDeviceTagger/commit/a6f224e
