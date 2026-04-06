---
title: "llm-agent: Stateful Agent wrapper"
status: open
priority: medium
created: 2026-04-06
---

# llm-agent: Stateful Agent wrapper

## Summary

Create a stateful `Agent` class that wraps the stateless `AgentLoop.RunAsync` with conversation memory, event subscription, and lifecycle management.

## Motivation

`AgentLoop.RunAsync` is deliberately stateless — the caller manages context, tools, and options. For applications that want a persistent agent (chat UIs, CLI sessions, background workers), this means reimplementing conversation management every time. pi-agent-core has `Agent` as a stateful wrapper over `agentLoop()` that manages the context, provides an event subscription model, and supports steering/follow-ups. This is the natural next layer.

## Proposal

### Goals

- `Agent` class that holds `AgentContext`, `AgentLoopOptions`, and `ILlmSdkClient`
- `RunAsync(prompt)` method that runs the loop and accumulates context across calls
- Event subscription model (callback or `IAsyncEnumerable` per run)
- Clean lifecycle: create → run → run again (multi-turn) → dispose

### Non-Goals

- Context persistence to disk/database (that's application-level)
- Dependency injection integration (Agent is created directly)
- Multi-agent coordination

## Design

`Agent` is a sealed class taking `ILlmSdkClient` and `AgentLoopOptions` in its constructor. It owns an `AgentContext` that persists across `RunAsync` calls. Each `RunAsync(prompt)` adds the user message to context, calls `AgentLoop.RunAsync`, and returns `IAsyncEnumerable<AgentEvent>`. The context grows across runs, enabling multi-turn conversations without the caller managing state. The `Agent` can optionally accept `IContextTransformer` to manage context growth.

## Tasks

- [ ] Define `Agent` class with constructor taking `ILlmSdkClient` + `AgentLoopOptions`
- [ ] Implement `RunAsync(string prompt)` → `IAsyncEnumerable<AgentEvent>`
- [ ] Context accumulates across multiple `RunAsync` calls
- [ ] Support `IDisposable` / `IAsyncDisposable` for cleanup
- [ ] TDD: two consecutive `RunAsync` calls share context
- [ ] TDD: Agent passes options through to AgentLoop
- [ ] TDD: Agent exposes current context for inspection

## Open Questions

- Should `Agent` support swapping tools between runs?
- Should `Agent` own cancellation (one token per run, or one for the agent lifetime)?
