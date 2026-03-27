# Copilot Instructions for llm-svc

## The Dependency Rule

This is the most important thing to understand. All source code dependencies point inward:

```
Infrastructure → Core ← llm-cli (via HTTP, not project reference)
                  ↑
              Program.cs (composition root)
```

**Core depends on nothing.** Not on Infrastructure, not on HTTP libraries, not on frameworks. Core defines port interfaces; Infrastructure implements them. `Program.cs` wires the two together. If you find yourself adding a `using` for anything outside `Core` inside a `Core/` file, you are violating the dependency rule. Stop.

**llm-cli is a separate deployable.** It talks to the proxy over HTTP. It never references llm-svc projects. It is a reference implementation — proof that the API works from a real client.

## The Boundaries

**Core/Ports/** — the interfaces. `IAuthProvider`, `IModelProvider`, `IResponsesService`. These are the contracts. When you need a new upstream capability, you define the abstraction here first. The interface belongs to the business logic, not to the adapter.

**Core/Services/** — the use cases. `ResponsesService` is the primary use case: it validates the request, resolves the model, and decides whether to pass through natively or translate through `ChatCompletionsTranslator`. The translator is a pure function — request in, response out. No HTTP, no I/O.

**Core/Models/** — data structures. Plain DTOs. No behavior. If you're tempted to add a method to a model, create a service instead.

**Infrastructure/** — the dirty details. `CopilotClient` does the HTTP. `CredentialManager` reads Windows credentials. `Worker` refreshes tokens. These classes implement the port interfaces and are the only place where external dependencies belong.

**Program.cs** — the composition root. This is the only file that knows about both Core and Infrastructure. All DI registration and endpoint mapping lives here. Keep it thin.

## The CLI Boundary

The CLI has its own clean structure:

**AskAgent** — the use case. It takes delegate functions for `createAsync` and `createStreamingAsync`, not a concrete `ResponsesClient`. This makes it testable without HTTP, without a running service, without mocks. Just lambdas and pure logic.

**ToolRegistry** — `ILocalTool` and `IToolRegistry` interfaces. New tools implement `ILocalTool`; the registry dispatches by name. `FetchUrlTool` is the first implementation.

**Tests fake behavior through function delegates**, not framework mocks. A queue of `ResponseResult` objects simulates multi-turn conversations. This is intentional — the test tells you exactly what happens, in order, with no mock framework magic hiding the intent.

## Build and Test

The proxy runs as a Windows Scheduled Task and **locks its binary**. Never build the full solution while it's running:

```powershell
# Target specific projects
dotnet test llm-cli.Tests\llm-cli.Tests.csproj --no-restore
dotnet test llm-svc.Tests\llm-svc.Tests.csproj --no-restore   # stop task first
```

Run a single test:

```powershell
dotnet test llm-cli.Tests --filter "FullyQualifiedName~RunNonStreamingAsync" --no-restore
```

Stop the task, build, restart:

```powershell
Stop-ScheduledTask -TaskName CopilotLlmProxy
# ... build/test the service ...
Start-ScheduledTask -TaskName CopilotLlmProxy
```

### Test Discipline

Tests are categorized by what they couple to:

- **Unit** — pure logic, fakes, no I/O. Always safe to run.
- **Integration** — `WebApplicationFactory` with fake providers. Safe to run, but exercises real HTTP pipeline in-memory.
- **Smoke** — tagged `[Trait("Category", "Smoke")]`. Hit `localhost:5100`. Require the running proxy. These prove the deployed system works.

```powershell
dotnet test llm-svc.Tests --filter "Category!=Smoke" --no-restore   # CI-safe
dotnet test --filter "Category=Smoke" --no-restore                   # live verification
```

Integration tests use `IClassFixture<ResponsesWebApplicationFactory>` — provider state is set per-test, not shared. Each test declares its own preconditions. CLI agent tests suppress `#pragma warning disable OPENAI001` (SDK preview surface).

## Conventions That Matter

**SSE line endings must be `\n`, never `\r\n`.** This broke compliance tests. Windows defaults will betray you.

**`JsonSerializerDefaults.Web`** for all JSON serialization. camelCase property names. The spec demands it.

**Event IDs** in `Core/LogEvents.cs` follow numbered ranges: 1xxx lifecycle, 2xxx auth, 3xxx API, 4xxx errors. New events go in the correct range.

**Versioning** — service and CLI version independently in `.csproj`. Git tags are for the service only (`v0.4.0`). Do not tag CLI changes.

**Conformance** — `backlog/002-Responses-conformance.json` tracks OpenResponses spec compliance. Items have priority (P0–P2) and Fibonacci complexity (1–21). Update status when you implement something. Mark upstream-only concerns `out_of_scope`.

**Style** — .NET 10, nullable enabled, `record` for DTOs, `GeneratedRegex` over `new Regex()`. Use `is null` / `is not null` over `== null` / `!= null`. Use `nameof` over string literals for member references. No `// Arrange // Act // Assert` comments in tests. Comments only when the code genuinely needs clarification. If you need a comment to explain what the code does, the code isn't clean enough.
