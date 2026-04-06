# 006 — llm-agent-core: Spec

## Overview

### Problem Statement

`llm-sdk` provides unified LLM access — auth, model routing, streaming, and translation between API shapes. But going from "I can talk to an LLM" to "I have an agent that does things" requires orchestration: extracting tool calls from responses, validating arguments, executing tools, feeding results back, and repeating until the model is done. This is the agent loop.

### Solution Summary

A new `llm-agent` package that implements the agent loop on top of `ILlmSdkClient`. It manages client-side context, streams lifecycle events for UI consumption, executes tools, and handles cancellation. The design follows `pi-agent-core` from `~/src/pi-mono/packages/agent` (spec: `~/src/macgyver/expertise/badlogic/pi-agent-core.md`), adapted to C#/.NET idioms and the Responses API shape.

### Business Value

| Benefit | Description |
|---------|-------------|
| Reusable agent runtime | Any .NET app can embed an agent with tool-calling support |
| UI-ready events | 10-event lifecycle system provides everything a responsive UI needs |
| Separation of concerns | Apps define tools and subscribe to events; the loop is handled |
| SDK-native | Uses `ILlmSdkClient` directly — no proxy required |

## User Stories

### US-1: App Developer Runs an Agent Loop

**As an** app developer using `llm-sdk`,
**I want to** hand the agent a prompt and a set of tools and get a complete response with tool calls handled,
**so that** I don't have to write the tool-call loop myself.

**Acceptance Criteria:**
- Agent loop takes `ILlmSdkClient`, a prompt, tools, and options
- Returns `IAsyncEnumerable<AgentEvent>` covering the full lifecycle
- Tool calls are extracted from `ResponseFunctionCallItem` in the response output
- Tool results are fed back as `ResponseFunctionCallOutputItem` in the next request's input
- Loop continues until no more tool calls or cancellation
- Errors during tool execution are reported to the model as error results

### US-2: UI Subscribes to Agent Events

**As a** UI developer,
**I want to** receive granular lifecycle events as the agent runs,
**so that** I can show streaming text, tool execution progress, and completion status.

**Acceptance Criteria:**
- Events cover agent start/end, turn start/end, message start/update/end, tool execution start/end
- Text deltas are forwarded via `message_update` events containing the `ResponseStreamEvent`
- Tool execution events include tool name, arguments, result, and error status
- Events are emitted in real-time as the stream progresses

### US-3: Tool Author Defines Executable Tools

**As a** tool author,
**I want to** define a tool with a JSON Schema, a description, and an execute function,
**so that** the agent can discover and call my tool when the model requests it.

**Acceptance Criteria:**
- `IAgentTool` extends `ResponseFunctionToolDefinition` with an execute method
- Execute receives the call ID, deserialized arguments, and a `CancellationToken`
- Execute returns a result (content string + optional details)
- If execute throws, the exception is caught and reported to the model as an error tool result

## Functional Requirements

| ID | Requirement | Description |
|----|-------------|-------------|
| FR-1 | Agent loop | Stateless function that runs the inner loop: stream → extract tool calls → execute → feed back → repeat |
| FR-2 | Client-side context | Context is a growing list of response items managed by the agent, serialized to `JsonElement` for `CreateResponseRequest.Input` |
| FR-3 | Tool execution | Sequential execution of all tool calls from a single response, with error handling |
| FR-4 | Event stream | `IAsyncEnumerable<AgentEvent>` with 10 event types covering agent/turn/message/tool lifecycle |
| FR-5 | Cancellation | `CancellationToken` propagated to streaming and tool execution |
| FR-6 | Tool not found | If model calls a tool not in the tools list, return error result to model |
| FR-7 | System prompt | Passed via `CreateResponseRequest.Instructions` |
| FR-8 | Model selection | Passed via `CreateResponseRequest.Model` |

## Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-1 | Single dependency | `llm-agent` depends only on `llm-sdk` (ProjectReference) |
| NFR-2 | No DI required | Agent loop is a static function taking explicit dependencies |
| NFR-3 | File count | ≤ 4 source files in V1 |
| NFR-4 | Namespace | `LlmAgent` root namespace |
| NFR-5 | Test isolation | All tests use `FakeLlmSdkClient` with delegate-based faking, no mock frameworks |

## Scope

### In Scope

- Stateless agent loop (`AgentLoop.RunAsync`)
- `IAgentTool` interface with execute
- `AgentEvent` discriminated union (10 event types)
- Client-side context management
- Sequential tool execution
- Cancellation support
- Unit tests with faked `ILlmSdkClient`

### Out of Scope (Future Phases)

- Stateful `Agent` wrapper class with steering/follow-up queues
- Before/after tool hooks
- Parallel tool execution mode
- `IProgress<T>` streaming from tool execution
- Context windowing / `transformContext` hook
- Custom message types beyond standard Responses API items
- Proxy stream function

## Assumptions

- `ILlmSdkClient.CreateResponseStreamAsync` reliably emits `ResponseCompletedEvent` or `ResponseFailedEvent` at end of stream
- `ResponseFunctionCallItem` in `ResponseCompletedEvent.Response.Output` contains fully-formed tool call data (name, call_id, arguments)
- `CreateResponseRequest.Input` accepts a `JsonElement` array containing mixed item types (message, function_call, function_call_output)

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `Input` serialization complexity with mixed item types | Medium | Medium | Test with actual API shapes; use existing `ResponseItem` polymorphic serialization |
| Upstream error handling during multi-turn loops | Low | High | Emit `agent_end` on any error; propagate via events, don't throw |
| Token expiry during long tool execution | Low | Medium | `ILlmSdkClient` already handles credential refresh internally |
