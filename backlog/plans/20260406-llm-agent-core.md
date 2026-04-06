---
title: "llm-agent: Agent loop and tool execution core"
status: completed
priority: high
created: 2026-04-06
---

# llm-agent: Agent loop and tool execution core

## Summary

Create a new `src/llm-agent/` package that provides agent loop orchestration on top of `llm-sdk`. The agent consumes `ILlmSdkClient` to stream LLM responses, extract tool calls, execute tools, feed results back, and repeat until done — with a real-time event system for UI consumption.

## Motivation

`llm-sdk` gets us halfway — unified LLM access with auth, model routing, and streaming. But there's a gap between "I can talk to an LLM" and "I have an agent that does things." That gap is the loop: find tool calls, validate arguments, execute, feed results back, keep going. `llm-agent` is the orchestration layer that bridges that gap.

## Reference Material

- **Pattern source**: `~/src/pi-mono/packages/agent` — the TypeScript agent-core that layers on `pi-ai`
- **Product spec**: `~/src/macgyver/expertise/badlogic/pi-agent-core.md` — reverse-engineered spec of `pi-agent-core` capabilities, actors, events, and edge cases

## Proposal

### Goals

- Stateless agent loop that streams `AgentEvent`s via `IAsyncEnumerable`
- `AgentTool` interface extending SDK tool definitions with execution logic
- Client-side context management (growing input array, no `previous_response_id`)
- Event system covering agent, turn, message, and tool lifecycle
- Abort/cancellation via `CancellationToken`

### Non-Goals

- Stateful `Agent` wrapper class (later phase — steering, follow-up queues)
- Before/after tool hooks (later phase)
- Parallel vs sequential tool execution modes (start with sequential)
- Proxy stream function (later phase)
- Built-in tools (app responsibility)
- Persistence (app responsibility)

## Design

The package mirrors `pi-agent-core`'s structure but in C#/.NET idioms. The seam is `ILlmSdkClient.CreateResponseStreamAsync()` → `IAsyncEnumerable<ResponseStreamEvent>`. The agent loop consumes these stream events, emits `AgentEvent`s for UI, and extracts `ResponseFunctionCallItem` from output to execute tools.

Context is managed as a list of typed input items that serialize to `CreateResponseRequest.Input` (a `JsonElement` array) each turn. Tool results are `ResponseFunctionCallOutputItem` appended to the context. No clean architecture layers — the package is ~4 files with clear responsibilities.

Dependency: `llm-agent` → `llm-sdk` (ProjectReference, imports `LlmSdk.Client` and `LlmSdk.Core.Models`). No infrastructure concerns.

## Tasks

- [ ] Create `src/llm-agent/llm-agent.csproj` with ProjectReference to `llm-sdk`
- [ ] Define `AgentTool` interface and `AgentEvent` discriminated union types
- [ ] Define context item types for client-side conversation management
- [ ] Implement `AgentLoop` — the stateless loop returning `IAsyncEnumerable<AgentEvent>`
- [ ] Add to solution and wire up `tests/llm-agent.Tests/` project
- [ ] Write unit tests with faked `ILlmSdkClient` (delegate-based, no mocks)

## Open Questions

- Should `AgentTool.ExecuteAsync` receive a progress callback (`IProgress<T>`) for streaming tool updates, or defer that to a later phase?
- How should the agent handle the `Input` serialization boundary — typed wrapper around `JsonElement`, or dedicated input item record types?
