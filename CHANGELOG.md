# Changelog

All notable changes to HybridDeviceTagger are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the format `<YYYY><MM>.<counter>` and are derived from the Git release tag `v<YYYY><MM>.<counter>`; builds outside `main` append the branch name, for example `202611.1-dev`.

## [Unreleased]

## [202611.2] - 2026-11-16

### Added

- Management module commands `Set-HybridDeviceTaggerServiceAccount`, `Get-HybridDeviceTaggerTrustedCertificate`, `Add-HybridDeviceTaggerTrustedCertificate`, and `Remove-HybridDeviceTaggerTrustedCertificate` for the post-installation configuration.
- The Function App root address `https://<function-app>.azurewebsites.net/` redirects to the configuration portal. Only the exact root path is excluded from App Service authentication; the redirect target is fixed and the response contains no data. All routes now carry the `api/` prefix explicitly (`routePrefix` is empty), so existing URLs are unchanged.
- The installation ends with the configuration portal URL.
- The README describes the post-installation configuration in four portal steps (upload the root certificate, configure the forest, set the user and password, create rules) with screenshots in `docs/images`.

### Changed

- Installation and configuration are separated: `Install.ps1` and `Install-HybridDeviceTagger.ps1` no longer prompt for the Active Directory forest, domain controller, service account, password, or LDAP trust certificate and no longer require the ActiveDirectory PowerShell module. Configure these values after installation in the configuration portal or with the management module. The AD parameters of the installation engine remain optional for unattended pre-seeding; updates without them keep the existing forest and service account.
- The Function starts without Active Directory settings. Scheduled tagging and password rotation are skipped until the forest domain and service account are configured; requests that need Active Directory return HTTP 409 with a message that names the missing setting and where to set it.
- The configuration portal guides the post-installation setup: without a stored service account, the LDAP tab names the next step, labels the account form "Set LDAP user", and disables LDAP validation and password change. The separate "Set initial password" box was removed; the account form sets the first account and password, and its UPN is prefilled with the stored account.
- Service endpoints on the FC1 integration subnet no longer stop the installation. The installer warns about each endpoint and reports its provisioning state and whether it covers the deployment region.
- The installation PC no longer needs access to the VNet DNS servers. Unreachable DNS servers only produce a warning; private endpoint DNS is verified from the Function VNet during code deployment.

### Fixed

- Validation and What-If no longer fail when the target resource group does not exist yet but `-CreateResourceGroup` is set; only the ARM steps that require the resource group are skipped. Previously the guided installation could not install into a new resource group.
- A failed Function code upload caused by an unreachable private storage endpoint is retried and then explains the required VNet DNS forwarding; interactive runs can retry after DNS is fixed.
## [202611.1] - 2026-11-15

First official release.

### Added

- Tagging of Microsoft Entra hybrid-joined devices based on their Active Directory OU: extension attributes, security groups, and administrative units.
- Secure LDAP access (LDAPS or StartTLS) with strict certificate validation and domain-controller failover.
- Automatic password rotation of the AD service account with Azure Key Vault.
- Configuration portal (German/English) for Function App Owners and Contributors: activity log, rules, LDAP, certificates, and schedules.
- Activity log in Log Analytics.
- Entra-protected management API and the PowerShell module `HybridDeviceTagger.Management`.
- Installation package with guided and unattended installation.

### Security

- Only Function App Owners and Contributors may add new Entra groups or administrative units as rule targets.
- Owner/Contributor access counts only for role assignments on the Function App or its parent scopes.
- Application Insights accepts only Entra-authenticated telemetry, so activity log entries cannot be forged.

### Changed

- The README version badge shows the version of every commit on every branch; the commit hooks update it and the commit guard and release build verify it.
