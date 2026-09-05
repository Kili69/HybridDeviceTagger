# Changelog

All notable changes to HybridDeviceTagger are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the project format `<major>.<minor>.<yyyyMMdd>.<counter>`.

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
