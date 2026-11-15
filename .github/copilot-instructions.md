# HybridDeviceTagger Workspace

- [x] Project requirements clarified: .NET 8 C# Azure Functions v4 isolated worker.
- [x] Scaffold the Function project in the workspace root.
- [x] Implement the AD-to-Entra device tagging workflow.
- [x] Add required tests and documentation.
- [x] Compile and test the project.
- [x] Publish the private GitHub repository under `Kili69`.

## Engineering Guidelines

- Keep Active Directory, Microsoft Graph, and orchestration concerns separated and testable.
- Use dependency injection and strongly typed configuration with startup validation.
- Use `DefaultAzureCredential`; never commit credentials or local settings.
- Make synchronization idempotent and update only devices whose `extensionAttribute1` differs from the configured value.
- Use structured logging without emitting credentials or sensitive directory data.
- Target Azure Functions v4 with the .NET 8 isolated worker model.
- The product version uses the format `<YYYY><MM>.<counter>` and is derived from the Git release tag `v<YYYY><MM>.<counter>` by `scripts/Get-ProductVersion.ps1`; outside `main` the branch name is appended. Never hard-code a product version in source files.
- Before every commit, describe the change below `## [Unreleased]` in `CHANGELOG.md`; never commit code changes without their history entry. For a release, move the entries to `## [<YYYY><MM>.<counter>] - <yyyy-MM-dd>` and run `scripts/Build-InstallationPackage.ps1 -Release` on `main`.
- Run `pwsh -NoProfile -File scripts/Assert-CommitMetadata.ps1 -Staged` after staging and before creating a commit.
- Start every production PowerShell script with its `#Requires` statements, `# Project-Version: source` (replaced with the product version by the package build), and `# Author: andreas.lucas@outlook.com (aka Kili)`, followed by the repository's standard Apache 2.0 license header.
- Keep all installation files in `installationspackage`; `scripts/Build-InstallationPackage.ps1` adds the prebuilt Function and creates the release archive.
- Document all production source code in English: XML documentation for every C# type and member, comment-based help for every PowerShell function, and descriptions for Bicep parameters, resources, and outputs.
- Start every production C# and Bicep source file with author `Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>` and the repository's standard Apache 2.0 license header. Test sources are excluded unless explicitly requested.
