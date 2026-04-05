# Copilot Instructions for llm-svc

**Start every session by reading `CONTRIBUTING.md`.** It has build commands, test categories, versioning rules, branch workflow, and available AI tooling (agents, hooks, skills). Do not duplicate that knowledge here — this file covers architecture and boundaries only.

## The Dependency Rule

This is the most important thing to understand. All source code dependencies point inward:

```
CopilotLlm/Infrastructure → CopilotLlm/Core ← llm-cli (via HTTP, not project reference)
                                 ↑
                     ServiceCollectionExtensions
                                 ↑
                         src/llm-svc/Program.cs
```

**src/CopilotLlm/Core depends on nothing.** Not on Infrastructure, not on HTTP libraries, not on frameworks. Core and Proxy define port interfaces; Infrastructure implements them. `ServiceCollectionExtensions` wires the library together, and `Program.cs` consumes the library from the host. If you find yourself adding a `using` for anything outside `Core` or `Proxy` inside a `src/CopilotLlm/Core/` or `src/CopilotLlm/Proxy/` file, you are violating the dependency rule. Stop.

**llm-cli is a separate deployable.** It talks to the proxy over HTTP. It never references llm-svc projects. It is a reference implementation — proof that the API works from a real client.

## The Boundaries

**CopilotLlm/Client/** — the SDK surface. `CopilotLlmClient`, `CopilotLlmOptions`, exceptions, and extension methods. These types are what NuGet consumers interact with directly.

**CopilotLlm/Proxy/** — the port interfaces. `IAuthProvider`, `IModelProvider`, `IResponsesService`. These are the contracts. When you need a new upstream capability, you define the abstraction here first. The interface belongs to the business logic, not to the adapter.

**CopilotLlm/Core/Services/** — the use cases. `ResponsesService` is the primary use case: it validates the request, resolves the model, and decides whether to pass through natively or translate through `ChatCompletionsTranslator`. The translator is a pure function — request in, response out. No HTTP, no I/O.

**CopilotLlm/Core/Models/** — data structures. Plain DTOs. No behavior. If you're tempted to add a method to a model, create a service instead.

**CopilotLlm/Infrastructure/** — the dirty details. `CopilotClient` does the HTTP. `CredentialManager` reads Windows credentials. Credential stores and D-Bus clients live here too. These classes implement the port interfaces and are the only place where external dependencies belong.

**ServiceCollectionExtensions.cs** — the library composition root. This is where the reusable DI wiring for Core and Infrastructure belongs.

**Program.cs** — the host composition root (in `src/llm-svc/`). It should stay thin: call `AddCopilotLlm()`, map endpoints, and register `Worker`.

## The CLI Boundary

The CLI has its own clean structure:

**AskAgent** — the use case. It takes delegate functions for `createAsync` and `createStreamingAsync`, not a concrete `ResponsesClient`. This makes it testable without HTTP, without a running service, without mocks. Just lambdas and pure logic.

**ToolRegistry** — `ILocalTool` and `IToolRegistry` interfaces. New tools implement `ILocalTool`; the registry dispatches by name. `FetchUrlTool` is the first implementation.

**Tests fake behavior through function delegates**, not framework mocks. A queue of `ResponseResult` objects simulates multi-turn conversations. This is intentional — the test tells you exactly what happens, in order, with no mock framework magic hiding the intent.

## Build and Test

See `CONTRIBUTING.md` for all build commands, test categories, and the file-lock warning. The critical rule: the proxy runs as a Windows Scheduled Task and **locks its binary** — never build the full solution while it's running. For library-only changes, target `src/CopilotLlm` and `tests/CopilotLlm.Tests` directly.

## Conventions That Matter

See `CONTRIBUTING.md` for the full list. The ones that have bitten us hardest:

- **SSE line endings must be `\n`, never `\r\n`.** This broke compliance tests.
- **`JsonSerializerDefaults.Web`** for all JSON. camelCase is spec-mandated.
- **`is null` / `is not null`** over `== null` / `!= null`.
- **No `// Arrange // Act // Assert` comments** in tests.
- If you need a comment to explain what the code does, the code isn't clean enough.
