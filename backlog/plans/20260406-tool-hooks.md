---
title: "llm-agent: Before/after tool hooks"
status: open
priority: medium
created: 2026-04-06
---

# llm-agent: Before/after tool hooks

## Summary

Add a hook pipeline to the agent loop that runs before and after each tool execution, enabling safety gates, argument redaction, result filtering, and observability.

## Motivation

As agents gain access to more powerful tools (file writes, shell commands, HTTP requests), there's no mechanism to intercept and block dangerous calls before execution or sanitize results after. pi-agent-core solves this with before/after hooks that can reject, modify, or audit tool calls. This is critical for production agent deployments.

## Proposal

### Goals

- Before-hook that can inspect tool name + arguments and approve, reject, or modify before execution
- After-hook that can inspect tool result and modify or redact before feeding back to the model
- Hooks are optional — zero hooks means current behavior unchanged
- Multiple hooks execute in registration order (pipeline pattern)

### Non-Goals

- Interactive approval UI (that's a consumer concern)
- Per-tool hook configuration (V1 is global hooks)
- Async hook pipelines with parallelism

## Design

Add `IAgentToolHook` interface with `BeforeExecuteAsync` and `AfterExecuteAsync` methods. `BeforeExecuteAsync` returns a decision (Allow, Reject with reason, or Modified arguments). `AfterExecuteAsync` can transform the `AgentToolResult`. Hooks are provided via `AgentLoopOptions.ToolHooks`. The loop calls them in sequence before/after `IAgentTool.ExecuteAsync`. A rejection short-circuits execution and returns an error result to the model.

## Tasks

- [ ] Define `IAgentToolHook` interface with `BeforeExecuteAsync` / `AfterExecuteAsync`
- [ ] Define `ToolHookDecision` (Allow / Reject / Modify) return type
- [ ] Add `ToolHooks` property to `AgentLoopOptions`
- [ ] Integrate hook pipeline into `AgentLoop.ExecuteToolAsync`
- [ ] Add `ToolHookRejected` or similar event to `AgentEvent` hierarchy
- [ ] TDD: hook rejects tool call → error result to model
- [ ] TDD: hook modifies arguments → tool receives modified args
- [ ] TDD: after-hook redacts result → model receives redacted output

## Open Questions

- Should hooks receive the full `AgentContext` for decision-making, or just the tool call?
- Should rejection emit a distinct `AgentEvent` subtype or reuse `ToolExecutionEnded` with `IsError`?
