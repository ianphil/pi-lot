# 006 — llm-agent-core: Plan

## Summary

Create a new `src/llm-agent/` package that implements the agent loop on top of `ILlmSdkClient`. The agent streams LLM responses, extracts tool calls, executes tools, feeds results back as context, and repeats — emitting lifecycle events for UI consumption. Client-side context management, no `previous_response_id`.

Adapted from `pi-agent-core` (`~/src/pi-mono/packages/agent`), per the reverse-engineered product spec at `~/src/macgyver/expertise/badlogic/pi-agent-core.md`.

## Architecture

```
App (defines tools, subscribes to events)
 │
 ▼
AgentLoop.RunAsync(client, prompt, options)
 │
 ├── ILlmSdkClient.CreateResponseStreamAsync()
 │       │
 │       ▼
 │   IAsyncEnumerable<ResponseStreamEvent>
 │       │
 │       ├── emit MessageStarted / MessageDelta / MessageEnded
 │       └── extract ResponseFunctionCallItem[] from completed response
 │
 ├── Execute IAgentTool[] (sequential)
 │       │
 │       ├── emit ToolExecutionStarted / ToolExecutionEnded
 │       └── build ResponseFunctionCallOutputItem results
 │
 ├── Append output + tool results to context
 └── Loop until no tool calls
 │
 ▼
IAsyncEnumerable<AgentEvent> (consumed by app/UI)
```

### Dependency

```
llm-agent → llm-sdk (ProjectReference)
  imports: LlmSdk.Client (ILlmSdkClient, ResponseStreamEvent)
           LlmSdk.Core.Models (CreateResponseRequest, ResponseItem, ResponseFunctionCallItem, ...)
  never:   LlmSdk.Proxy, LlmSdk.Infrastructure
```

## File Structure

```
src/llm-agent/                      NEW
├── AgentTypes.cs                    IAgentTool, AgentEvent hierarchy, AgentLoopOptions, AgentToolResult
├── AgentContext.cs                  AgentContext, AgentContextItem hierarchy, serialization
├── AgentLoop.cs                     Static RunAsync — the loop
└── llm-agent.csproj                 ProjectReference → llm-sdk

tests/llm-agent.Tests/               NEW
├── Fakes/
│   ├── FakeLlmSdkClient.cs         Delegate-based fake (adapted from llm-cli.Tests)
│   └── FakeAgentTool.cs             Delegate-based IAgentTool
├── Helpers/
│   └── StreamHelpers.cs             Factory for canned ResponseStreamEvent sequences
├── Unit/
│   ├── AgentEventTests.cs           Type hierarchy, pattern matching
│   ├── AgentToolTests.cs            IAgentTool → ResponseFunctionToolDefinition conversion
│   └── AgentLoopTests.cs            All loop behavior tests
└── llm-agent.Tests.csproj
```

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Separate package | `src/llm-agent/` | Mirrors pi-mono's `ai` / `agent` separation; independent versioning |
| `ILlmSdkClient` as seam | Client surface, not proxy ports | Agent is a library consumer; SDK client is already the right abstraction |
| Client-side context | Typed `AgentContext` with `AgentContextItem` hierarchy | Full control over context; serialize to `JsonElement` only at boundary |
| No `convertToLlm` step | Direct serialization | Responses API input IS the LLM format — simpler than pi-agent-core |
| Sequential tool execution only (V1) | `foreach` over tool calls | Parallel adds complexity; sequential is correct and simple |
| Stateless loop only (V1) | `AgentLoop.RunAsync()` static method | Stateful `Agent` wrapper is a V2 concern |
| Record hierarchy for events | Abstract record + sealed records | Exhaustive pattern matching; immutable; C# idiomatic (9 event types) |
| Tool args as `JsonElement` | Loop parses once, tools get typed args | Avoids duplicated JSON parsing in every tool |
| Delegate-based test fakes | `FakeLlmSdkClient` + `FakeAgentTool` | Matches existing test patterns; no mock framework |

## Implementation Phases

### Phase 1: Project Setup (T001–T004)
Scaffold `llm-agent.csproj` and `llm-agent.Tests.csproj`, add to solution, verify build.

### Phase 2: Types & Contracts (T005–T016)
Define the type system: `AgentContext` with typed items and serialization, `AgentEvent` hierarchy, `IAgentTool` (with `JsonElement` args), `AgentToolResult`, `AgentLoopOptions`. Create test fakes and helpers.

### Phase 3: Agent Loop (T017–T041)
TDD the core loop: single-turn → tool execution → error handling (including `ResponseIncompleteEvent`) → loop control → request building. This is the bulk of the work.

### Phase 4: Integration & Validation (T042–T044)
End-to-end multi-turn scenarios with faked client. Full build validation.

## Verification

1. `dotnet build src/llm-agent/llm-agent.csproj` — agent project compiles
2. `dotnet test tests/llm-agent.Tests/llm-agent.Tests.csproj --no-restore` — all tests pass
3. `dotnet build copilot-llm.sln` — no regressions to existing projects

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `Input` serialization of mixed item types | Test with actual Responses API JSON shapes in stream helpers |
| Context growth unbounded | `MaxTurns` safety valve; context windowing deferred to V2 |
| Upstream API changes to tool call format | Types are already defined in llm-sdk; changes would break existing SDK tests first |

## Limitations (V1)

- No stateful `Agent` wrapper — no steering, follow-up queues, or event subscription model
- No parallel tool execution — sequential only
- No before/after tool hooks
- No context windowing or `transformContext`
- No `IProgress<T>` streaming from tool execution
- No custom message types
