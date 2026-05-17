---
name: sdk-change
description: 'Use this for LlmSdk work: SDK issues, SDK client changes, Core/Proxy behavior, Infrastructure adapters, model/auth/routing/translation changes, or any task that mentions the SDK layer. It forces an SDK layer decision first, keeps agent/service consumers separate, and picks the owning SDK tests before implementation.'
---

# SDK Change Workflow

Use this skill when implementing, reviewing, or planning changes under
`src/llm-sdk/` or when a user asks for an "SDK" capability. The goal is to avoid
treating "SDK" as one blob.

Always read `CONTRIBUTING.md`, `.github/copilot-instructions.md`, and the
applicable scoped instructions before editing:

- `.github/instructions/sdk-boundaries.instructions.md`
- `.github/instructions/sdk-client.instructions.md`
- `.github/instructions/testing.instructions.md`
- `.github/instructions/integration-tests.instructions.md` when adding `.Int`
  coverage
- `.github/instructions/wire-format.instructions.md` when changing JSON, HTTP,
  SSE, routing, or translation

## Step 1 - Identify the SDK layer

Before changing code, name the owning layer:

| Layer | Owns | Primary consumers |
|---|---|---|
| `Client/` | Ergonomic in-process SDK API: `ILlmSdkClient`, `Context`, `CompletionOptions`, `AssistantMessage`, `AssistantStreamEvent`, raw `CreateResponse*` wrappers | SDK package consumers, `llm-agent`, `llm-ui` |
| `Core/` | Shared business logic: routing, translation, validation, normalization, DTOs, pure services | SDK client and `llm-svc` |
| `Proxy/` | Port contracts such as `IResponsesService`, `IModelProvider`, auth/model abstractions | SDK client, service host, adapters |
| `Infrastructure/` | External adapters: Copilot HTTP, credentials, auth refresh, model discovery, OS integration | SDK DI composition and hosts |
| `ServiceCollectionExtensions.cs` | Reusable SDK DI wiring | `llm-svc`, SDK consumers, tests |

If the requested behavior is only for the ergonomic in-process client, keep it
in `Client/` and prove it through SDK client tests. If it must affect both the
client and `llm-svc`, put the shared semantics in `Core/` or `Proxy/` so both
paths consume the same behavior. If it touches external systems, isolate that
work in `Infrastructure/`.

## Step 2 - Preserve boundaries

- `Core/` and `Proxy/` must not reference Infrastructure, HTTP clients, ASP.NET
  Core, host projects, CLI, UI, or agent code.
- `Core/Models/` are DTOs. Put behavior in services, translators, validators, or
  request normalization helpers.
- Define or update port contracts before changing adapters for new upstream
  capabilities.
- Do not modify `llm-agent`, `llm-svc`, `llm-cli`, or `llm-ui` just because they
  might eventually consume the SDK change. Create a follow-up unless the issue
  explicitly changes that consumer's behavior.

## Step 3 - Choose tests by owning surface

Use the narrowest existing tests that prove the owning SDK layer:

| Change | Default tests |
|---|---|
| SDK units, DTOs, translators, validators, adapters | `dotnet test tests/llm-sdk.Tests/llm-sdk.Tests.csproj --no-restore` |
| Public SDK client behavior or reference-consumer flow | `dotnet test tests/llm-sdk.Int --filter "Category!=Smoke" --no-restore` |
| Live SDK compatibility check | `dotnet test tests/llm-sdk.Int --filter "Category=Smoke" --no-restore` |
| Direct upstream capture/drift documentation | `dotnet test tests/llm-upstream.Int --filter "Category=UpstreamCapture" --no-restore` |

When adding important SDK integration behavior, invoke the `fake-live-int-tests`
skill and create paired fake/live coverage unless the user explicitly scopes the
work to unit-only behavior.

## Step 4 - Implement deliberately

1. Add or update unit tests for pure SDK logic first when practical.
2. Implement in the owning SDK layer.
3. Add fake `.Int` coverage when the public SDK surface should prove the flow.
4. Add a small live smoke only for real-upstream compatibility.
5. Update SDK docs when the public surface or behavior changes.

## Step 5 - Explain the boundary in the handoff

In the final summary or PR description, say which SDK layer owned the behavior
and which tests prove it. This helps reviewers see that agent/service consumers
were not accidentally conflated with the SDK client surface.
