# Contributing to llm-svc

This guide is written for AI agents and human contributors alike.
Rules are explicit and unambiguous — follow them literally.

## Project Structure

**llm-svc** is the primary project — a local proxy that translates OpenAI Responses API
requests to upstream providers. **llm-cli** is a reference implementation client that
demonstrates how to consume the proxy. Changes to the proxy are the main concern;
CLI changes support or demonstrate proxy capabilities.

```
llm-svc/
├── Program.cs                         Composition root, endpoint wiring
├── Core/                              Domain logic (no external dependencies)
│   ├── LogEvents.cs                   Structured event IDs for Windows Event Log
│   ├── Models/                        DTOs, request/response types, helpers
│   ├── Ports/                         Interfaces (IAuthProvider, IModelProvider, IResponsesService)
│   └── Services/                      Translation, serialization, business logic
├── Infrastructure/                    External adapters (HTTP, credentials, hosting)
│   ├── CopilotClient.cs              HTTP adapter to upstream Copilot API
│   ├── CredentialManager.cs           Windows Credential Manager access
│   └── Worker.cs                      Background auth lifecycle
├── llm-cli/                           Reference CLI client
│   ├── Program.cs                     Entry point (System.CommandLine)
│   ├── AskAgent.cs                    Agent loop with tool-calling support
│   ├── FetchUrlTool.cs                Built-in fetch_url tool
│   └── ToolRegistry.cs               Tool registration and dispatch
├── llm-svc.Tests/                     Service tests
│   ├── Fakes/                         Test doubles
│   ├── Unit/                          Pure logic tests
│   ├── Integration/                   WebApplicationFactory tests
│   └── Smoke/                         Live endpoint tests (Category=Smoke)
├── llm-cli.Tests/                     CLI tests
│   └── Smoke/                         Live CLI tests (Category=Smoke)
├── docs/                              API reference, event log guide, compliance
├── backlog/                           Conformance matrix (JSON)
└── scripts/                           Install/uninstall for scheduled task and CLI
```

## Architecture Rules

The proxy uses a hexagonal (ports & adapters) pattern:

- **Core/** has zero references to Infrastructure or external HTTP libraries.
  All external concerns are behind interfaces in `Core/Ports/`.
- **Infrastructure/** implements those interfaces and is wired in `Program.cs`.
- **Models/** are plain DTOs. Do not add behavior to model classes.
- When adding a new upstream capability, define the port interface first,
  then implement the adapter in Infrastructure.

The CLI is self-contained in `llm-cli/` and depends only on the OpenAI .NET SDK.
It does not reference `llm-svc` projects — it talks to the proxy over HTTP.

## Build and Test

### ⚠️ File-Lock Warning

The proxy typically runs as a Windows Scheduled Task. While it is running,
`llm-svc.exe` is locked. Do not run `dotnet build` or `dotnet test` against
the solution or the `llm-svc` / `llm-svc.Tests` projects while the task is active.

```powershell
# WRONG — will fail if proxy is running
dotnet test llm-svc.sln

# RIGHT — build and test CLI projects independently
dotnet test llm-cli.Tests\llm-cli.Tests.csproj --no-restore
```

To build/test the service, stop the scheduled task first:

```powershell
Stop-ScheduledTask -TaskName CopilotLlmProxy
dotnet test llm-svc.Tests\llm-svc.Tests.csproj --no-restore
Start-ScheduledTask -TaskName CopilotLlmProxy
```

### Test Categories

| Category | Scope | Requires Running Proxy | Safe in Isolation |
|---|---|---|---|
| Unit | Pure logic, fakes | No | Yes |
| Integration | WebApplicationFactory | No | Yes |
| Smoke | Live HTTP to `localhost:5100` | **Yes** | No |
| Compliance | OpenResponses spec suite | **Yes** | No |

Run CI-safe tests (unit + integration):

```powershell
dotnet test llm-svc.Tests --filter "Category!=Smoke" --no-restore
dotnet test llm-cli.Tests --filter "Category!=Smoke" --no-restore
```

Run smoke tests (proxy must be running):

```powershell
dotnet test --filter "Category=Smoke" --no-restore
```

### What to Test Before Submitting

- If you changed **Core/** or **Infrastructure/**: run `llm-svc.Tests` (stop task first).
- If you changed **llm-cli/**: run `llm-cli.Tests`.
- If you changed response serialization or translation: run smoke tests against both
  a GPT model (native `/responses`) and a Claude model (translated from `/chat/completions`).

## Versioning

The service and CLI are versioned **independently** in their respective `.csproj` files:

```xml
<!-- llm-svc.csproj -->
<Version>0.4.0</Version>

<!-- llm-cli/llm-cli.csproj -->
<Version>0.2.0</Version>
```

**Rules:**

- Both follow [SemVer 2.0](https://semver.org/).
- Bump the version in the `.csproj` when shipping a meaningful change.
- **Git tags are for the service only.** Tag format: `v{version}` (e.g., `v0.4.0`).
- Do not create git tags for CLI-only changes.
- A PR that changes both projects may bump both versions but only tags the service version.

**When to bump:**

| Change | Bump |
|---|---|
| Bug fix, minor tweak | Patch (`0.4.0` → `0.4.1`) |
| New feature, endpoint, or capability | Minor (`0.4.0` → `0.5.0`) |
| Breaking API change | Major (`0.4.0` → `1.0.0`) |

## Branch and PR Workflow

- Branch from `main`. Use `feature/` prefix for new work (e.g., `feature/streaming-tools`).
- Squash merge PRs into `main`.
- Delete the feature branch after merge.
- Do not tag on merge — tags are created manually at release time.

## Installing / Upgrading the Service

The install and uninstall scripts require an **elevated (Administrator) PowerShell**:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\scripts\install.ps1"
```

This stops any running instance, publishes the build, registers the scheduled task,
and starts the proxy. See `README.md` for management commands.

## Conformance Backlog

`backlog/002-Responses-conformance.json` tracks OpenResponses API spec conformance.

Each requirement has:

- **status**: `implemented`, `partial`, `not_implemented`, or `out_of_scope`
- **priority**: `P0` (core/agent-critical), `P1` (robust conformance), `P2` (nice-to-have)
- **complexity**: Fibonacci scale — `1` (trivial), `2` (small), `5` (medium), `8` (large), `13`/`21` (epic)

When implementing a conformance item, update its `status` and add notes describing
what was done. Do not remove items — mark upstream-only concerns as `out_of_scope`
with an explanation.

## Documentation

- **docs/api-reference.md** — Canonical API surface. Update when adding or changing endpoints.
- **docs/event-log-guide.md** — Windows Event Log structure. Update when adding event IDs.
- **docs/how-to-run-compliance-tests.md** — Compliance test runner guide.
- **README.md** — Project overview and quick start. Keep in sync with major changes.

## Code Style

- .NET 10, C# latest, nullable enabled.
- No comments unless the code requires clarification.
- Use `record` types for simple DTOs.
- Use `GeneratedRegex` over `new Regex()`.
- Use `JsonSerializerDefaults.Web` for camelCase JSON serialization.
- Use `is null` / `is not null` over `== null` / `!= null`.
- Use `nameof` over string literals for member references.
- No `// Arrange // Act // Assert` comments in tests.
- SSE output must use `\n` line endings, never `\r\n`.

## AI Tooling

This repo ships agents, hooks, and skills in `.github/` for use with GitHub Copilot and compatible AI tools. When starting a session, consider whether any of these are relevant to the task at hand.

### Agents (`.github/agents/`)

| Agent | Purpose |
|---|---|
| **uncle-bob** | Principal engineer guidance channeling Robert C. Martin — Clean Code, Clean Architecture, SOLID. Tuned to this codebase's dependency rule and conventions. |
| **csharp-dotnet-janitor** | Code cleanup and modernization — unused usings, naming fixes, pattern matching, performance. Respects our architectural boundaries. |
| **doublecheck** | Verification specialist — extracts claims from AI output, finds sources, flags risks. Three-layer pipeline: self-audit, source verification, adversarial review. |

### Hooks (`.github/hooks/`)

| Hook | Trigger | Purpose |
|---|---|---|
| **secrets-scanner** | `sessionEnd` | Scans modified files for 20+ secret/credential patterns. Warn mode by default; set `SCAN_MODE=block` to prevent commits. |

### Skills (`.github/skills/`)

| Skill | Purpose |
|---|---|
| **csharp-xunit** | XUnit best practices — `[Fact]`/`[Theory]`, `IClassFixture`, data-driven tests, assertion patterns. |
