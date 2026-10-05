# HybridDeviceTagger Workspace

- [x] Project requirements clarified: .NET 8 C# Azure Functions v4 isolated worker.
- [x] Scaffold the Function project in the workspace root.
- [x] Implement the AD-to-Entra device tagging workflow.
- [x] Add required tests and documentation.
- [x] Compile and test the project.
- [x] Publish the private GitHub repository under `anluca_microsoft`.

## Engineering Guidelines

- Keep Active Directory, Microsoft Graph, and orchestration concerns separated and testable.
- Use dependency injection and strongly typed configuration with startup validation.
- Use `DefaultAzureCredential`; never commit credentials or local settings.
- Make synchronization idempotent and update only devices whose `extensionAttribute1` differs from the configured value.
- Use structured logging without emitting credentials or sensitive directory data.
- Target Azure Functions v4 with the .NET 8 isolated worker model.
- Before every commit, increment the product version in `Directory.Build.props` using the `<major>.<minor>.<yyyyMMdd>.<counter>` format.
- Before every commit, add a dated section for that exact version to `CHANGELOG.md`; never commit code changes without their history entry.
- Run `pwsh -NoProfile -File scripts/Assert-CommitMetadata.ps1 -Staged` after staging and before creating a commit.
- Start every production PowerShell script with its `#Requires` statements, `# Project-Version: <version>` matching `Directory.Build.props`, and `# Author: andreas.lucas@outlook.com (aka Kili)`, followed by the repository's standard Apache 2.0 license header.
- Start every production C# and Bicep source file with author `Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>` and the repository's standard Apache 2.0 license header. Test sources are excluded unless explicitly requested.