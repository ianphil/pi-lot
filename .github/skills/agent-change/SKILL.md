---
name: agent-change
description: 'Use this for LlmAgent work: agent loop changes, AgentSession/harness APIs, AgentLoopOptions/API design, tools, events, context budget, option forwarding, or any task that mentions the agent package. It forces a harness-first public-surface decision, prefers portable SDK context streaming for ergonomic agent behavior, keeps raw Responses as an explicit escape hatch, and picks unit/fake/live agent tests before implementation.'
---

# Agent Change Workflow

Use this skill when implementing, reviewing, or planning changes under
`src/llm-agent/` or when a user asks for an "agent" capability. The goal is to
build `LlmAgent` into an ergonomic agent harness library, not a protocol-faithful
raw Responses wrapper or a loose pass-through around every SDK option.

The reference direction is the TypeScript `@earendil-works/pi-agent-core`
package under `~/src/pi/packages/agent`: a stateful agent/harness with
conversation state, portable messages, lifecycle events, tool orchestration,
queues, sessions, compaction, hooks, and runtime configuration. Do not copy that
implementation blindly, but use its product shape to guide `LlmAgent` API
decisions.

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
| `AgentLoop` | Run loop, turn sequencing, portable SDK request construction, tool execution | `tests/llm-agent.Tests` |
| `AgentSession` / harness APIs | Stateful transcript, queues, in-flight state, abort, settle, runtime configuration | `tests/llm-agent.Tests` plus `.Int` for public SDK-consumer flows |
| `AgentLoopOptions` | Public run controls, request metadata, callbacks, cancellation-related knobs | `tests/llm-agent.Tests` plus `.Int` for public behavior |
| `IAgentTool` and tool results | Tool schema, invocation, validation, streamed arguments, tool outputs | `tests/llm-agent.Tests` |
| `AgentEvent` | Public event stream, partials, diagnostics, tool lifecycle events | `tests/llm-agent.Tests` and `.Int` for observable flows |
| `AgentContext` and budget | Portable conversation state, context updates, model-capability and token-budget behavior | `tests/llm-agent.Tests` |

If the requested behavior is purely SDK-owned, use the `sdk-change` skill
instead. If the agent only consumes a public SDK feature, keep the agent change
to the agent package and prove the consumption through agent tests.

## Step 2 - Design public API deliberately

- Treat `LlmAgent` as a public package API. Small option additions are still API
  design decisions.
- Prefer harness-native concepts over broad SDK passthrough. Agent users should
  think in terms of transcript, session, messages, tools, turns, events, queues,
  stop reasons, diagnostics, and runtime configuration.
- Prefer portable SDK context concepts for harness-facing behavior:
  `Context`, `AssistantMessage`, `AssistantStreamEvent`, `ToolCallDelta`,
  `UsageEvent`, `StopReason`, `ThinkingLevel`, `CacheRetention`, diagnostics,
  and `CompletionOptions` concepts when they are intentionally adopted.
- When choosing between raw `CreateResponseStreamAsync(CreateResponseRequest)`
  and portable `StreamAsync(Context, CompletionOptions)`, default to
  `StreamAsync(Context)` for agent-harness work. Issue #94 is the migration
  decision point: the desired direction is an ergonomic harness built on
  portable SDK streaming.
- Keep raw Responses types such as `ResponseStreamEvent`, `Response`,
  `ResponseItem`, raw payload hooks, and protocol-specific fields as explicit
  advanced/debug/compatibility escape hatches. Do not make them the primary
  public model unless the issue specifically requires protocol fidelity.
- Use precise names for raw Responses-native behavior only when preserving a raw
  escape hatch. For example, `PromptCacheKey` is raw; broader cache/session
  behavior should use portable names such as `CacheRetention` and `SessionId`
  when the story owns those semantics.
- Keep behavioral capabilities such as abort modes, thinking controls, cache
  retention policy, diagnostics, and context migration in explicit API-design
  stories. Do not add them incidentally while forwarding unrelated options.
- Document callback timing. Agent loops may issue multiple SDK requests, so
  hooks must be specified and tested as per-turn or per-run.

## Step 3 - Preserve boundaries

- Do not move behavior into the SDK just because the agent needs it. Shared
  semantics belong below the SDK boundary only when multiple public surfaces
  actually consume them.
- Let the agent consume SDK portable APIs and wrap them in agent-native harness
  concepts. If the SDK portable surface cannot preserve needed harness behavior,
  document the gap and either keep a narrow raw escape hatch or split a new SDK
  issue; do not silently rebuild SDK-owned semantics inside `llm-agent`.
- Do not modify `llm-svc`, `llm-cli`, or `llm-ui` for an agent issue unless the
  issue explicitly changes that package's owned behavior.
- Keep service/proxy wire DTOs separate from agent harness abstractions.

## Step 4 - Choose tests by owning behavior

Use the narrowest existing tests that prove the agent behavior:

| Change | Default tests |
|---|---|
| Pure loop, event-shape, tool, context, or budget logic | `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore` |
| Public agent behavior as an SDK consumer | `dotnet test tests/llm-agent.Int --filter "Category!=Smoke" --no-restore` |
| Live agent compatibility check | `dotnet test tests/llm-agent.Int --filter "Category=Smoke" --no-restore` |
| SDK-owned behavior discovered during agent work | Switch to `sdk-change` and prove it in SDK tests |

For #94-style migration decisions, tests or executable repros should prove that
the chosen path preserves the harness-visible behavior: transcript entries, tool
calls, tool results, streamed text, thinking, usage, diagnostics, partials,
stop reasons, terminal state, and final context.

When adding important agent integration behavior, invoke the
`fake-live-int-tests` skill and create paired fake/live coverage unless the user
explicitly scopes the work to unit-only behavior.

## Step 5 - Implement deliberately

1. Add or update unit tests first for deterministic loop, event, tool, context,
   session, harness, or budget behavior.
2. Implement in the owning agent surface.
3. Add fake `.Int` coverage when the public agent API should prove the flow
   through `ILlmSdkClient`.
4. Add a small live smoke only for real-upstream compatibility.
5. Update `docs/agent-guide.md` when public API or behavior changes.

## Step 6 - Explain the API boundary in the handoff

In the final summary or PR description, say which agent surface owned the
behavior and classify the API as one of:

- harness-native behavior,
- portable SDK consumption through `Context` / `StreamAsync`,
- raw Responses escape hatch.

State why that classification is correct and which tests prove it. This helps
reviewers see that SDK features were intentionally adopted for the agent harness
instead of accidentally converted into agent package commitments or hidden raw
protocol dependencies.
