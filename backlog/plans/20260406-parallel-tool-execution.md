---
title: "llm-agent: Parallel tool execution"
status: open
priority: low
created: 2026-04-06
---

# llm-agent: Parallel tool execution

## Summary

Add an option to execute multiple tool calls from a single response concurrently rather than sequentially, with sequential preflight validation.

## Motivation

When the model returns multiple independent tool calls in one response (e.g., fetch 5 URLs), sequential execution is unnecessarily slow. pi-agent-core supports parallel execution as default with sequential as an option. The Responses API defaults `parallel_tool_calls: true`, signaling that the model expects concurrent execution.

## Proposal

### Goals

- Add `ParallelToolExecution` option to `AgentLoopOptions` (default: false for backward compat)
- Preflight all tool calls sequentially (validate name, parse args, run before-hooks if present)
- Execute approved tools concurrently via `Task.WhenAll`
- Maintain deterministic event ordering (all `ToolExecutionStarted` before all `ToolExecutionEnded`)

### Non-Goals

- Configurable concurrency limits (V1 is unbounded)
- Per-tool parallelism opt-out
- Streaming results from parallel tools

## Design

When `ParallelToolExecution` is true and multiple `ResponseFunctionCallItem` exist in the response output, the loop first validates all calls sequentially (tool lookup, argument parsing, before-hooks). Then it launches all valid executions as concurrent tasks via `Task.WhenAll`. Results are collected and appended to context in the original order. Events are emitted: all `ToolExecutionStarted` first, then all `ToolExecutionEnded` as they complete.

## Tasks

- [ ] Add `ParallelToolExecution` bool to `AgentLoopOptions`
- [ ] Extract preflight validation into a separate method
- [ ] Implement concurrent execution path with `Task.WhenAll`
- [ ] Ensure deterministic context ordering regardless of completion order
- [ ] TDD: parallel mode executes tools concurrently (verify via timing or execution tracking)
- [ ] TDD: sequential mode unchanged (backward compat)
- [ ] TDD: one tool fails in parallel → others still complete, error fed back
