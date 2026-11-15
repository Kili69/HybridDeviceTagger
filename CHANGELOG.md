# Changelog

All notable changes to HybridDeviceTagger are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the format `<YYYY><MM>.<counter>` and are derived from the Git release tag `v<YYYY><MM>.<counter>`; builds outside `main` append the branch name, for example `202611.1-dev`.

## [Unreleased]

### Changed

- The README version badge shows the version of every commit on every branch; the commit hooks update it and the commit guard and release build verify it.

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
