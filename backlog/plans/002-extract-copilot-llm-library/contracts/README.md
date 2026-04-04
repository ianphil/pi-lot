# CopilotLlm Library Contracts

Interface definitions for the CopilotLlm library.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [di-extension.md](di-extension.md) | AddCopilotLlm() DI extension contract |
| [package-metadata.md](package-metadata.md) | NuGet package configuration |

## Contract Principles

- The library exposes Core/Ports interfaces as its public API surface
- ServiceCollectionExtensions is the only new public API
- All existing interfaces (IAuthProvider, IModelProvider, IResponsesService, IChatCompletionsService) are unchanged
- No hosting abstractions leak into the library
