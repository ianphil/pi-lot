# Contributing to pi-lot

This guide is the contributor-facing source for repository workflow, build and
test commands, releases, and documentation. Code-generation and review guidance
lives in scoped Copilot instruction files under `.github/instructions/`.

## Project Structure

This repository ships five related components:

| Component | Path | Purpose |
|---|---|---|
| LlmSdk | `src/llm-sdk/` | Reusable .NET library for auth, model discovery, routing, and translation |
| llm-svc | `src/llm-svc/` | Local OpenAI-compatible HTTP proxy host |
| llm-cli | `src/llm-cli/` | Terminal reference client for the proxy |
| llm-agent | `src/llm-agent/` | Tool-calling agent loop built on the SDK |
| llm-ui | `src/llm-ui/` | Experimental editable-context chat UI |

Source projects live under `src/`, test projects under `tests/`, shared build
settings live in `Directory.Build.props`, and the root solution is
`pi-lot.sln`.

## Build and Test

### File-Lock Warning

The proxy typically runs as a Windows Scheduled Task. While it is running,
`llm-svc.exe` is locked. Do not run `dotnet build` or `dotnet test` against the
solution or the `llm-svc` / `llm-svc.Tests` projects while the task is active.
Target library, CLI, agent, or UI projects directly when the service is running.

```powershell
# WRONG if the proxy scheduled task is running
dotnet test pi-lot.sln

# RIGHT for library-only changes
dotnet test tests\llm-sdk.Tests\llm-sdk.Tests.csproj --no-restore
dotnet test tests\llm-sdk.Int --filter "Category!=Smoke" --no-restore
```

To build or test the service, stop the scheduled task first:

```powershell
Stop-ScheduledTask -TaskName LlmProxy
dotnet test tests\llm-svc.Tests\llm-svc.Tests.csproj --no-restore
Start-ScheduledTask -TaskName LlmProxy
```

### Test Categories

| Category | Scope | Requires Running Proxy | Safe in Isolation |
|---|---|---|---|
| Unit | Pure logic and fakes | No | Yes |
| Integration | In-process integration such as `WebApplicationFactory` or fake reference consumers | No | Yes |
| Smoke | Live product checks through SDK, agent, service, or CLI surfaces | Varies by test | No |
| UpstreamCapture | Direct upstream API capture/snapshot drift checks | No | No |
| Compliance | OpenResponses spec suite | Yes | No |

Run CI-safe tests with:

```powershell
dotnet test tests\llm-sdk.Tests --no-restore
dotnet test tests\llm-sdk.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-agent.Tests --no-restore
dotnet test tests\llm-agent.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-svc.Tests --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-svc.Int --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-cli.Tests --filter "Category!=Smoke" --no-restore
dotnet test tests\llm-ui.Tests --filter "Category!=Smoke" --no-restore
```

For a whole-solution CI-safe run, exclude live product smoke tests and direct
upstream capture drift checks:

```powershell
dotnet test pi-lot.sln --filter "Category!=Smoke&Category!=UpstreamCapture" --no-restore
```

Run smoke tests only when credentials, internet access, and any required local
proxy are available:

```powershell
dotnet test --filter "Category=Smoke" --no-restore
```

Run upstream capture drift checks separately when intentionally validating or
refreshing direct upstream API captures:

```powershell
dotnet test tests\llm-upstream.Int --filter "Category=UpstreamCapture" --no-restore
```

Run the UI SPA smoke tests from the client app:

```powershell
Push-Location src\llm-ui\ClientApp
npm run smoke
Pop-Location
```

### Test Project Ownership

Put tests in the project that owns the changed behavior:

| Changed surface | Test project |
|---|---|
| SDK client, Core models/services, Infrastructure adapters | `tests/llm-sdk.Tests/` |
| SDK fake/live reference-consumer behavior | `tests/llm-sdk.Int/` |
| Service host, HTTP endpoints, service wiring | `tests/llm-svc.Tests/` |
| Service fake/live proxy behavior against host wiring | `tests/llm-svc.Int/` |
| CLI commands, CLI agents, local tools | `tests/llm-cli.Tests/` |
| Agent loop, events, context budget | `tests/llm-agent.Tests/` |
| Agent fake/live behavior against SDK client surface | `tests/llm-agent.Int/` |
| Browser UI behavior | `tests/llm-ui.Tests/` |
| Upstream Copilot API captures and drift detection | `tests/llm-upstream.Int/` |

For implementation-specific testing rules, see
`.github/instructions/testing.instructions.md` and
`.github/instructions/integration-tests.instructions.md`.

## Versioning and Releases

`LlmSdk` and `LlmAgent` are the only released packages today. `llm-svc`,
`llm-cli`, and `llm-ui` are local/reference projects until a separate release
decision is made.

Package versions are managed at release time. Feature PRs should not bump
`.csproj` versions; those versions represent the last shipped stable package
version.

```xml
<!-- src/llm-sdk/llm-sdk.csproj -->
<Version>0.7.0</Version>

<!-- src/llm-agent/llm-agent.csproj -->
<Version>0.1.0</Version>
```

Both released packages follow [SemVer 2.0](https://semver.org/). Releases are
manual and package-specific.

| Package | Tag format | Example |
|---|---|---|
| `LlmSdk` | `sdk-v{version}` | `sdk-v0.7.0` |
| `LlmAgent` | `agent-v{version}` | `agent-v0.1.0` |

See `docs/release-channels.md` for the release-channel model and deferred
surfaces.

### Package Publishing

`LlmSdk` currently publishes to GitHub Packages using
`.github/workflows/publish-llm-sdk.yml`. `LlmAgent` publishing is planned but not
implemented yet.

- Run publishing workflows manually with `workflow_dispatch`.
- The workflow publishes to `https://nuget.pkg.github.com/{owner}/index.json`
  using `GITHUB_TOKEN`.

## Branch and PR Workflow

- Branch from `main`.
- Use a descriptive branch name, usually with `feature/`, `fix/`, or `chore/`.
- Keep PRs small and independent; do not stack PRs in this repo.
- Squash merge PRs into `main`.
- Delete the feature branch after merge.
- Do not tag on merge; tags are created manually at release time.

## Installing / Upgrading the Service

The install and uninstall scripts require an elevated Administrator PowerShell:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\scripts\install.ps1"
```

The install script stops any running instance, publishes the build, registers the
scheduled task, and starts the proxy. See `README.md` for management commands.

## Conformance Backlog

`backlog/002-Responses-conformance.json` tracks OpenResponses API spec
conformance.

Each requirement has:

- `status`: `implemented`, `partial`, `not_implemented`, or `out_of_scope`
- `priority`: `P0` for core/agent-critical, `P1` for robust conformance, or `P2`
  for nice-to-have
- `complexity`: Fibonacci scale from `1` through `21`

When implementing a conformance item, update its status and notes. Do not remove
items; mark upstream-only concerns as `out_of_scope` with an explanation. See
`.github/instructions/backlog.instructions.md` for scoped editing rules.

## Documentation

| File | Purpose |
|---|---|
| `README.md` | Project overview and quick start |
| `docs/sdk-guide.md` | LlmSdk setup, API usage, streaming, and error handling |
| `docs/agent-guide.md` | LlmAgent loop, tool authoring, event stream, and examples |
| `docs/cli-guide.md` | CLI commands, flags, tool calling, and examples |
| `docs/api-reference.md` | Proxy HTTP API surface |
| `docs/event-log-guide.md` | Windows Event Log structure |
| `docs/how-to-run-compliance-tests.md` | Compliance test runner guide |
| `backlog/002-Responses-conformance.json` | OpenResponses conformance backlog |

Update the relevant documentation when changing user-facing behavior, HTTP
endpoints, event IDs, CLI flags, SDK APIs, or conformance status.

## Copilot Instructions

This repo uses scoped instruction files in `.github/instructions/`. They are the
code-generation and review rule source for specific files and surfaces:

| File | Scope |
|---|---|
| `sdk-boundaries.instructions.md` | SDK dependency rule and package boundaries |
| `sdk-client.instructions.md` | SDK public client surface |
| `service.instructions.md` | `llm-svc` host and service tests |
| `cli.instructions.md` | `llm-cli` commands, agents, and tools |
| `agent.instructions.md` | `llm-agent` loop and integration behavior |
| `ui.instructions.md` | `llm-ui` host, SPA, and dependency guidance |
| `testing.instructions.md` | Test ownership and unit-test conventions |
| `integration-tests.instructions.md` | Fake/live `.Int` suite pattern |
| `upstream-captures.instructions.md` | Direct upstream capture contracts |
| `wire-format.instructions.md` | JSON, SSE, and HTTP wire-format rules |
| `workflows.instructions.md` | GitHub Actions workflow conventions |
| `docs.instructions.md` | Markdown and docs maintenance |
| `backlog.instructions.md` | Conformance backlog editing rules |
| `csharp-style.instructions.md` | C# style conventions |

## AI Tooling

This repo ships agents, hooks, and skills in `.github/` for GitHub Copilot and
compatible AI tools.

| Type | Name | Purpose |
|---|---|---|
| Agent | `uncle-bob` | Clean Code, Clean Architecture, SOLID guidance |
| Agent | `csharp-dotnet-janitor` | C#/.NET cleanup and modernization |
| Agent | `doublecheck` | Verification and adversarial review |
| Hook | `secrets-scanner` | Scans modified files for secret-like values at session end |
| Skill | `agent-change` | Agent public API, loop, tool, event, and context-budget workflow |
| Skill | `csharp-xunit` | XUnit best practices |
| Skill | `fake-live-int-tests` | Paired fake/live `.Int` test workflow |
| Skill | `issue-slate` | Batch workflow for grouped GitHub issues |
| Skill | `sdk-change` | SDK layer decision, implementation, and validation workflow |
| Skill | `skill-creator` | Create and improve reusable skills |
