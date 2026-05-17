---
description: 'Maintain LlmSdk dependency direction and package boundaries.'
applyTo: 'src/llm-sdk/**/*.cs'
---

# SDK Boundaries

Keep source dependencies pointing inward:

```text
Infrastructure -> Core <- Client
              Proxy contracts
```

- `Core/` owns business logic, translation, validation, request normalization,
  and plain DTOs. It must not reference Infrastructure, HTTP libraries, ASP.NET
  Core, deployable projects, or UI/CLI/agent code.
- `Proxy/` owns port interfaces such as `IResponsesService`, `IModelProvider`,
  and auth/model abstractions. Interfaces belong here before adapters implement
  new upstream capabilities.
- `Infrastructure/` owns external adapters: HTTP, credential stores, upstream
  clients, operating-system integration, and other dirty details.
- `ServiceCollectionExtensions.cs` is the reusable library composition root.
  Host-specific wiring belongs in the host, not in the SDK.
- `Core/Models/` types are DTOs. Do not add behavior to models; create or update
  a service instead.

When adding upstream behavior, define the boundary first, implement the adapter
second, and keep shared semantics in Core services or translators.
