# Copilot Instructions for llm-svc

## What This Is

A local proxy that translates OpenAI Responses API requests to GitHub Copilot's upstream LLM API. Two projects live in this repo:

- **llm-svc** — the proxy service (primary project). Hexagonal architecture: `Core/` has domain logic behind port interfaces, `Infrastructure/` implements adapters, `Program.cs` wires everything.
- **llm-cli** — a reference CLI client that talks to the proxy over HTTP. Self-contained, does not reference llm-svc projects.

## Build and Test

The proxy runs as a Windows Scheduled Task (`CopilotLlmProxy`) and **locks its binary while running**. Never build or test the full solution while the task is active:

```powershell
# WRONG — fails if proxy is running
dotnet test llm-svc.sln
dotnet build

# RIGHT — target specific projects
dotnet test llm-cli.Tests\llm-cli.Tests.csproj --no-restore
dotnet test llm-svc.Tests\llm-svc.Tests.csproj --no-restore   # stop task first
```

To run a single test:

```powershell
dotnet test llm-cli.Tests --filter "FullyQualifiedName~RunNonStreamingAsync" --no-restore
```

Smoke tests require the proxy running on `localhost:5100`:

```powershell
dotnet test --filter "Category=Smoke" --no-restore
```

Stop/start the scheduled task when you need to rebuild the service:

```powershell
Stop-ScheduledTask -TaskName CopilotLlmProxy
# ... build/test ...
Start-ScheduledTask -TaskName CopilotLlmProxy
```

## Architecture

### Proxy (llm-svc)

Hexagonal / ports-and-adapters:

- **Core/Ports/** — interfaces (`IAuthProvider`, `IModelProvider`, `IResponsesService`). Core has zero references to Infrastructure or HTTP libraries.
- **Core/Services/** — translation logic. `ResponsesService` routes requests: models supporting `/responses` natively get passthrough; chat-only models (Claude, etc.) go through `ChatCompletionsTranslator` / `ChatCompletionsStreamTranslator`.
- **Core/Models/** — plain DTOs only, no behavior.
- **Infrastructure/** — adapters: `CopilotClient` (HTTP to upstream API), `CredentialManager` (Windows Credential Manager), `Worker` (background token refresh).
- **Program.cs** — composition root. All DI wiring and endpoint mapping lives here.

When adding a new upstream capability: define the port interface in `Core/Ports/` first, then implement the adapter in `Infrastructure/`.

### CLI (llm-cli)

- `Program.cs` — System.CommandLine entry point with `ask`, `models`, `health` commands.
- `AskAgent.cs` — agent loop supporting tool calls. Delegates to `ResponsesClient` (OpenAI .NET SDK).
- `ToolRegistry.cs` — `ILocalTool` / `IToolRegistry` interfaces. `LocalToolRegistry.CreateDefault()` registers built-in tools.
- `FetchUrlTool.cs` — built-in `fetch_url` tool: HTTP GET → strip HTML → truncate → structured JSON.

Agent tests fake the SDK by passing lambda delegates for `createAsync` and `createStreamingAsync` instead of mocking `ResponsesClient`.

## Key Conventions

**Serialization:** Use `JsonSerializerDefaults.Web` for camelCase JSON. SSE output must use `\n` line endings — never `\r\n` (breaks spec compliance).

**Event IDs:** `Core/LogEvents.cs` uses 4-digit ranges: 1xxx lifecycle, 2xxx auth, 3xxx API, 4xxx errors. Add new IDs in the correct range.

**Testing patterns:**
- Integration tests use `IClassFixture<ResponsesWebApplicationFactory>` with a fake `IModelProvider` whose state is set per-test.
- Smoke tests are tagged `[Trait("Category", "Smoke")]` and hit `localhost:5100` directly.
- CLI agent tests use `#pragma warning disable OPENAI001` (SDK preview warning).

**Versioning:** Service (v0.4.0) and CLI (v0.2.0) version independently in their `.csproj` files. Git tags (`v0.4.0`) are for the service only — do not tag CLI-only changes.

**Conformance tracking:** `backlog/002-Responses-conformance.json` tracks OpenResponses spec compliance with P0/P1/P2 priority and Fibonacci complexity (1–21). Update `status` when implementing a requirement; mark upstream-only concerns `out_of_scope`.

**Code style:** .NET 10, nullable enabled, `record` for DTOs, `GeneratedRegex` over `new Regex()`, no comments unless clarification is genuinely needed.
