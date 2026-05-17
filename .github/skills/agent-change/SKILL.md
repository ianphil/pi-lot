---
name: agent-change
description: 'Use this for LlmAgent work: agent loop changes, AgentLoopOptions/API design, tools, events, context budget, option forwarding, or any task that mentions the agent package. It forces an agent public-surface decision first, keeps SDK passthrough separate from agent-native behavior, and picks unit/fake/live agent tests before implementation.'
---

# Agent Change Workflow

Use this skill when implementing, reviewing, or planning changes under
`src/llm-agent/` or when a user asks for an "agent" capability. The goal is to
keep `LlmAgent` a deliberate NuGet package surface instead of a loose wrapper
around every SDK option.

Always read `CONTRIBUTING.md`, `.github/copilot-instructions.md`, and the
applicable scoped instructions before editing:

- `.github/instructions/agent.instructions.md`
- `.github/instructions/testing.instructions.md`
- `.github/instructions/integration-tests.instructions.md` when adding `.Int`
  coverage
- `.github/instructions/sdk-client.instructions.md` when changing how the agent
  consumes `ILlmSdkClient`
- `.github/instructions/wire-format.instructions.md` when changing JSON, SSE,
  raw Responses items, or HTTP-adjacent payloads

## Step 1 - Identify the agent surface

Before changing code, name the agent surface being changed:

| Surface | Owns | Default tests |
|---|---|---|
| `AgentLoop` | Run loop, turn sequencing, SDK request construction, tool execution | `tests/llm-agent.Tests` |
| `AgentLoopOptions` | Public run controls, request metadata, callbacks, cancellation-related knobs | `tests/llm-agent.Tests` plus `.Int` for public behavior |
| `IAgentTool` and tool results | Tool schema, invocation, validation, streamed arguments, tool outputs | `tests/llm-agent.Tests` |
| `AgentEvent` | Public event stream, partials, diagnostics, tool lifecycle events | `tests/llm-agent.Tests` and `.Int` for observable flows |
| `AgentContext` and budget | Raw conversation state, context updates, token-budget behavior | `tests/llm-agent.Tests` |

If the requested behavior is purely SDK-owned, use the `sdk-change` skill
instead. If the agent only consumes a public SDK feature, keep the agent change
to the agent package and prove the consumption through agent tests.

## Step 2 - Design public API deliberately

- Treat `LlmAgent` as a public package API. Small option additions are still API
  design decisions.
- Prefer agent-native concepts over broad SDK passthrough. Forward raw SDK
  fields only when the public name accurately describes current behavior.
- Use precise names for raw Responses-native behavior, such as
  `PromptCacheKey`, until a story explicitly owns broader portable session,
  cache, or context semantics.
- Keep behavioral capabilities such as abort modes, thinking controls, cache
  retention policy, diagnostics, and context migration in explicit API-design
  stories. Do not add them incidentally while forwarding unrelated options.
- Document callback timing. Agent loops may issue multiple SDK requests, so
  hooks must be specified and tested as per-turn or per-run.

## Step 3 - Preserve boundaries

- Do not move behavior into the SDK just because the agent needs it. Shared
  semantics belong below the SDK boundary only when multiple public surfaces
  actually consume them.
- Do not modify `llm-svc`, `llm-cli`, or `llm-ui` for an agent issue unless the
  issue explicitly changes that package's owned behavior.
- Keep raw Responses-native state raw until a migration story intentionally
  introduces portable context or event abstractions.

## Step 4 - Choose tests by owning behavior

Use the narrowest existing tests that prove the agent behavior:

| Change | Default tests |
|---|---|
| Pure loop, event-shape, tool, context, or budget logic | `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore` |
| Public agent behavior as an SDK consumer | `dotnet test tests/llm-agent.Int --filter "Category!=Smoke" --no-restore` |
| Live agent compatibility check | `dotnet test tests/llm-agent.Int --filter "Category=Smoke" --no-restore` |
| SDK-owned behavior discovered during agent work | Switch to `sdk-change` and prove it in SDK tests |

When adding important agent integration behavior, invoke the
`fake-live-int-tests` skill and create paired fake/live coverage unless the user
explicitly scopes the work to unit-only behavior.

## Step 5 - Implement deliberately

1. Add or update unit tests first for deterministic loop, event, tool, context,
   or budget behavior.
2. Implement in the owning agent surface.
3. Add fake `.Int` coverage when the public agent API should prove the flow
   through `ILlmSdkClient`.
4. Add a small live smoke only for real-upstream compatibility.
5. Update `docs/agent-guide.md` when public API or behavior changes.

## Step 6 - Explain the API boundary in the handoff

In the final summary or PR description, say which agent surface owned the
behavior, whether the API is agent-native or raw SDK forwarding, and which tests
prove it. This helps reviewers see that SDK features were not accidentally
converted into agent package commitments.
