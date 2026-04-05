# Contributing to llm-svc

This guide is written for AI agents and human contributors alike.
Rules are explicit and unambiguous — follow them literally.

## Project Structure

**llm-svc** is the primary deployable host — a local proxy that translates OpenAI
Responses API requests to upstream providers. **LlmSdk** is the reusable class
library that contains the translation engine, auth, model discovery, and upstream HTTP
adapter. **llm-cli** is a reference implementation client that demonstrates how to
consume the proxy. Changes to the host or library are the main concern; CLI changes
support or demonstrate proxy capabilities.

```
llm-svc/
├── src/
│   ├── llm-sdk/                        Reusable library (packable NuGet)
│   │   ├── ServiceCollectionExtensions.cs DI entry point for hosts
│   │   ├── Client/                        SDK surface (LlmSdkClient, options, exceptions)
│   │   ├── Proxy/                         Public port interfaces (IResponsesService, IModelProvider)
│   │   ├── Core/                          Domain logic (no external dependencies)
│   │   │   ├── LogEvents.cs               Structured event IDs for Windows Event Log
│   │   │   ├── Models/                    DTOs, request/response types, helpers
│   │   │   └── Services/                  Translation, serialization, business logic
│   │   └── Infrastructure/                External adapters (HTTP, credentials)
│   │       ├── CopilotClient.cs           HTTP adapter to upstream Copilot API
│   │       └── CredentialManager.cs       Windows Credential Manager access
│   ├── llm-svc/                           Host proxy
│   │   ├── Program.cs                     Composition root and endpoint wiring
│   │   └── Worker.cs                      Background auth lifecycle
│   └── llm-cli/                           Reference CLI client
│       ├── Program.cs                     Entry point (System.CommandLine)
│       ├── AskAgent.cs                    Agent loop with tool-calling support
│       ├── FetchUrlTool.cs                Built-in fetch_url tool
│       └── ToolRegistry.cs               Tool registration and dispatch
├── tests/
│   ├── llm-sdk.Tests/                 Library unit tests
│   │   └── Unit/                          Pure library tests
│   ├── llm-svc.Tests/                     Service tests
│   │   ├── Fakes/                         Test doubles
│   │   ├── Integration/                   WebApplicationFactory tests
│   │   └── Smoke/                         Live endpoint tests (Category=Smoke)
│   └── llm-cli.Tests/                     CLI tests
│       └── Smoke/                         Live CLI tests (Category=Smoke)
├── docs/                                  API reference, event log guide, compliance
├── backlog/                               Conformance matrix (JSON)
├── scripts/                               Install/uninstall for scheduled task and CLI
├── Directory.Build.props                  Shared build properties (TFM, nullable)
└── copilot-llm.sln
```

## Architecture Rules

The proxy and library use a hexagonal (ports & adapters) pattern:

- **llm-sdk/Core/** has zero references to Infrastructure or external HTTP libraries.
  All external concerns are behind interfaces in `Core/Ports/`.
- **llm-sdk/Infrastructure/** implements those interfaces and is wired by
  `ServiceCollectionExtensions.AddLlmSdk()`.
- **Models/** are plain DTOs. Do not add behavior to model classes.
- When adding a new upstream capability, define the port interface first,
  then implement the adapter in Infrastructure.

Source projects live under `src/`, test projects under `tests/`. The root
`Directory.Build.props` holds shared build settings (TFM, nullable, implicit usings).

`llm-svc` is a thin host: `src/llm-svc/Program.cs` calls `AddLlmSdk()`, maps HTTP
endpoints, and registers `Worker`. Keep hosting concerns there; keep reusable
logic in `src/llm-sdk/`.

The CLI is self-contained in `src/llm-cli/` and depends only on the OpenAI .NET SDK.
It does not reference `llm-svc` projects — it talks to the proxy over HTTP.

## Build and Test

### ⚠️ File-Lock Warning

The proxy typically runs as a Windows Scheduled Task. While it is running,
`llm-svc.exe` is locked. Do not run `dotnet build` or `dotnet test` against
the solution or the `llm-svc` / `llm-svc.Tests` projects while the task is active.
Target `LlmSdk` / `llm-sdk.Tests` directly for library-only changes.

```powershell
# WRONG — will fail if proxy is running
dotnet test copilot-llm.sln

# RIGHT — build and test library / CLI projects independently
dotnet test tests\llm-sdk.Tests\llm-sdk.Tests.csproj --no-restore
dotnet test tests\llm-cli.Tests\llm-cli.Tests.csproj --no-restore
```

To build/test the service, stop the scheduled task first:

```powershell
Stop-ScheduledTask -TaskName LlmProxy
dotnet test tests\llm-svc.Tests\llm-svc.Tests.csproj --no-restore
Start-ScheduledTask -TaskName LlmProxy
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
dotnet test tests\llm-sdk.Tests --no-restore
dotnet test tests\llm-svc.Tests --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-cli.Tests --filter "Category!=Smoke" --no-restore
```

Run smoke tests (proxy must be running):

```powershell
dotnet test --filter "Category=Smoke" --no-restore
```

### What to Test Before Submitting

- If you changed **src/llm-sdk/**: run `llm-sdk.Tests`.
- If you changed **src/llm-svc/Program.cs** or **Worker.cs**: run `llm-svc.Tests` (stop task first).
- If you changed **src/llm-cli/**: run `llm-cli.Tests`.
- If you changed response serialization or translation: run smoke tests against both
  a GPT model (native `/responses`) and a Claude model (translated from `/chat/completions`).

## Versioning

The library, service, and CLI are versioned **independently** in their respective `.csproj` files:

```xml
<!-- src/llm-sdk/llm-sdk.csproj -->
<Version>0.1.0</Version>

<!-- src/llm-svc/llm-svc.csproj -->
<Version>0.6.0</Version>

<!-- src/llm-cli/llm-cli.csproj -->
<Version>0.3.0</Version>
```

**Rules:**

- All three follow [SemVer 2.0](https://semver.org/).
- Bump the version in the `.csproj` when shipping a meaningful change.
- Each component uses a prefixed tag format:

| Component | Tag format | Example |
|---|---|---|
| Service | `svc-v{version}` | `svc-v0.6.0` |
| Library | `lib-v{version}` | `lib-v0.1.0` |
| CLI | `cli-v{version}` | `cli-v0.3.0` |

- A PR that changes multiple components may bump multiple versions and use
  multiple tag formats when releasing them separately.
- Filter tags by component: `git tag -l "svc-v*"`, `git tag -l "lib-v*"`,
  `git tag -l "cli-v*"`.
- Legacy tags using bare `v{version}` (before this convention) refer to service
  releases. They are kept as-is; new service releases use `svc-v{version}`.

**When to bump:**

| Change | Bump |
|---|---|
| Bug fix, minor tweak | Patch (`0.4.0` → `0.4.1`) |
| New feature, endpoint, or capability | Minor (`0.4.0` → `0.5.0`) |
| Breaking API change | Major (`0.4.0` → `1.0.0`) |

### Library Publishing

`LlmSdk` publishes to GitHub Packages using `.github/workflows/publish-copilotllm.yml`.

- Push a tag matching the library version, like `lib-v0.1.0`, to publish automatically.
- Or run the workflow manually with **workflow_dispatch** to publish the current library version.
- The workflow publishes to `https://nuget.pkg.github.com/{owner}/index.json` using `GITHUB_TOKEN`.

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
