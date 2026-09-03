# HybridDeviceTagger Workspace

- [x] Project requirements clarified: .NET 8 C# Azure Functions v4 isolated worker.
- [x] Scaffold the Function project in the workspace root.
- [x] Implement the AD-to-Entra device tagging workflow.
- [x] Add required tests and documentation.
- [x] Compile and test the project.
- [ ] Publish the private GitHub repository under `anluca_microsoft`.

## Engineering Guidelines

- Keep Active Directory, Microsoft Graph, and orchestration concerns separated and testable.
- Use dependency injection and strongly typed configuration with startup validation.
- Use `DefaultAzureCredential`; never commit credentials or local settings.
- Make synchronization idempotent and update only devices whose `extensionAttribute1` differs from the configured value.
- Use structured logging without emitting credentials or sensitive directory data.
- Target Azure Functions v4 with the .NET 8 isolated worker model.