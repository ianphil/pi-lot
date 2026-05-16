# Contributing to llm-svc

This guide is written for AI agents and human contributors alike.
Rules are explicit and unambiguous — follow them literally.

## Project Structure

**llm-svc** is the primary deployable host — a local proxy that translates OpenAI
Responses API requests to upstream providers. **LlmSdk** is the reusable class
library that contains the translation engine, auth, model discovery, and upstream HTTP
adapter. **llm-cli** is a thin smoke/reference client for the proxy. Changes to
the host or library are the main concern; CLI changes should stay limited to the
live matrix-backed command surface.

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
│   └── llm-cli/                           Proxy smoke/reference CLI client
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

The CLI is self-contained in `src/llm-cli/`. Its `ask` / `chat` commands use the
OpenAI .NET SDK to talk to the proxy over HTTP. It intentionally does not expose
direct SDK commands; validate SDK behavior in `llm-sdk.Int` and service/proxy
behavior in `llm-svc.Int`.

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
| Smoke | Live upstream/API or proxy checks | Varies by test | No |
| Compliance | OpenResponses spec suite | **Yes** | No |

Run CI-safe tests (unit + integration):

```powershell
dotnet test tests\llm-sdk.Tests --no-restore
dotnet test tests\llm-agent.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-svc.Tests --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-svc.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-cli.Tests --filter "Category!=Smoke" --no-restore
```

Run smoke tests (may require Copilot credentials, internet access, and sometimes
a running proxy depending on the test):

```powershell
dotnet test --filter "Category=Smoke" --no-restore
```

### Unit and Integration Test Location

Place unit and integration tests under the test project that owns the changed
surface:

| Changed surface | Test project |
|---|---|
| `src/llm-sdk/` SDK client, Core models/services, Infrastructure adapters | `tests/llm-sdk.Tests/` |
| `src/llm-svc/` host, HTTP endpoints, service wiring | `tests/llm-svc.Tests/` |
| `src/llm-svc/` fake/live proxy behavior against real host wiring | `tests/llm-svc.Int/` |
| `src/llm-cli/` commands, CLI agents, local tools | `tests/llm-cli.Tests/` |
| `src/llm-agent/` agent loop, agent events, context budget | `tests/llm-agent.Tests/` |
| `src/llm-agent/` fake/live agent loop behavior against SDK client surface | `tests/llm-agent.Int/` |
| `src/llm-ui/` browser UI behavior | `tests/llm-ui.Tests/` |
| Upstream Copilot API capture docs and drift detection | `tests/llm-upstream.Int/` |

Do not put tests in `llm-agent.Tests` just because a feature touches prompts,
streaming, tools, or context. Use `llm-agent.Tests` only for the agent library.
SDK behavior belongs in `llm-sdk.Tests`; CLI command behavior belongs in
`llm-cli.Tests`; service endpoint behavior belongs in `llm-svc.Tests`.

Test doubles belong in the owning test project, usually under `Fakes/`. For
SDK consumer tests that use the portable `Context` API, prefer scripted
`ILlmSdkClient` fakes that record the `Context` / `CompletionOptions` request
and return scripted `AssistantMessage` or `AssistantStreamEvent` sequences.
Keep those helpers internal to the test project that owns the consumer; do not
create separate test-support projects unless the repo explicitly adopts one.

### SDK Integration Test Pattern

Use `tests/llm-sdk.Int` for SDK reference-consumer scenarios. Important SDK
capabilities should prefer paired tests:

- a deterministic fake-provider test that exercises the real SDK API surface
  without live upstream calls
- a live smoke test that exercises the same SDK API surface against the real
  Copilot API

The fake-provider test is the CI-friendly correctness check. The live test is
the upstream compatibility check and should be marked `Category=Smoke`.

### Upstream API Capture Pattern

Use `tests/llm-upstream.Int` for direct Copilot upstream capture contracts.
These tests bypass `LlmSdk`, `llm-svc`, `llm-agent`, and `llm-cli` request
abstractions. They use existing credential loading only to obtain a token, then
call `https://api.enterprise.githubcopilot.com` directly.

The committed snapshots are living upstream capability documentation. Capture
advertised positive surfaces and useful negative probes. Redact secrets and
credential-like values only; do not normalize IDs, timestamps, model revisions,
usage counts, response headers, SSE payloads, websocket messages, unknown fields,
or other upstream details just because this repo does not consume them yet.

All upstream capture tests are `Category=Smoke`. Refresh snapshots only when
intentionally documenting accepted upstream drift:

```powershell
$env:LLM_UPSTREAM_UPDATE_SNAPSHOTS = "1"
dotnet test tests\llm-upstream.Int --filter "Category=Smoke"
```

### Agent Integration Test Pattern

Use `tests/llm-agent.Int` for agent-loop behavior that benefits from fake/live
pairing:

- a deterministic fake-SDK-client test for multi-turn event flow, tool
  execution, context updates, and option forwarding
- a live smoke test that runs the same public `AgentLoop` surface through the
  real SDK client and Copilot upstream

Keep pure event-shape and edge-case coverage in `llm-agent.Tests`. Use
`llm-agent.Int` when the test should prove the public agent API remains a
working reference consumer of `ILlmSdkClient`.

### Service Integration Test Pattern

Use `tests/llm-svc.Int` for service/proxy behavior that benefits from the same
fake/live pairing:

- a deterministic `WebApplicationFactory` test with fake SDK proxy ports for
  endpoint routing, option forwarding, and translation behavior
- a live smoke test against the same HTTP endpoint shape and real Copilot
  upstream

Keep service/proxy correctness here instead of expanding the CLI matrix. The CLI
matrix should stay a small user-facing smoke suite for command parsing, process
execution, local tools, and endpoint connectivity.

### E2E Test Matrix

The `scripts/test-matrix.*` scripts are CLI smoke validation, not unit or
integration tests. Every row must run a real CLI command against the real proxy,
use real Copilot auth, and call an actual LLM model.

Fakes, mocks, stubs, `WebApplicationFactory`, test servers, and `dotnet test`
are fine in `Unit` and `Integration` tests, but they do not belong in the test
matrix execution path. If SDK behavior needs fake/live coverage, prefer paired
`llm-sdk.Int` tests instead of adding CLI matrix rows. If agent behavior needs
fake/live coverage, prefer paired `llm-agent.Int` tests. If service/proxy
behavior needs fake/live coverage, prefer paired `llm-svc.Int` tests. If CLI
behavior needs matrix coverage, expose a real CLI path that exercises the
production code and live upstream behavior.

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
<Version>0.3.1</Version>

<!-- src/llm-svc/llm-svc.csproj -->
<Version>0.6.1</Version>

<!-- src/llm-cli/llm-cli.csproj -->
<Version>0.4.0</Version>
```

**Rules:**

- All three follow [SemVer 2.0](https://semver.org/).
- Bump the version in the `.csproj` when shipping a meaningful change.
- Each component uses a prefixed tag format:

| Component | Tag format | Example |
|---|---|---|
| Service | `svc-v{version}` | `svc-v0.6.1` |
| Library | `lib-v{version}` | `lib-v0.3.1` |
| CLI | `cli-v{version}` | `cli-v0.4.0` |

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

`LlmSdk` publishes to GitHub Packages using `.github/workflows/publish-llm-sdk.yml`.

- Push a tag matching the library version, like `lib-v0.3.1`, to publish automatically.
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

- **docs/sdk-guide.md** — LlmSdk library guide: setup, API usage, streaming, error handling.
- **docs/agent-guide.md** — LlmAgent guide: agent loop, tool authoring, event stream, examples.
- **docs/cli-guide.md** — CLI reference: commands, flags, tool calling, examples.
- **docs/api-reference.md** — Proxy HTTP API surface. Update when adding or changing endpoints.
- **docs/event-log-guide.md** — Windows Event Log structure. Update when adding event IDs.
- **docs/how-to-run-compliance-tests.md** — Compliance test runner guide.
- **README.md** — Project overview and quick start. Keep in sync with major changes.
- **src/llm-ui/ClientApp/package.json** — SPA dependency manifest. Pin direct dependencies to exact versions and run `npm outdated` after dependency changes.
- **src/llm-ui/ClientApp/tests/** — Playwright UI smoke tests for the editable Markdown chat experience. Run with `npm run smoke` from `src/llm-ui/ClientApp`.
- **src/llm-agent/AgentContextBudget.cs** — Context budget guard. It estimates prompt tokens with `Microsoft.ML.Tokenizers` and treats upstream `token_limits` metadata as the source of model budgets.

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
