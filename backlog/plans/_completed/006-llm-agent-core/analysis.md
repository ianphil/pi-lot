# 006 — llm-agent-core: Analysis

## Executive Summary

| Pattern | Integration Point | Status |
|---------|-------------------|--------|
| SDK client surface | `ILlmSdkClient` — streaming responses, typed events | ✅ Built |
| Tool definition | `ResponseFunctionToolDefinition` — schema declaration | ✅ Built |
| Response model | `Response`, `ResponseItem`, `ResponseFunctionCallItem` | ✅ Built |
| Stream event parsing | `ResponseStreamEvent` hierarchy — 20+ event types | ✅ Built |
| Agent loop | Orchestration: stream → tools → feed back → repeat | ❌ Needed |
| Agent tool interface | `IAgentTool` — schema + execute function | ❌ Needed |
| Agent events | Lifecycle events for UI consumption | ❌ Needed |
| Client-side context | Typed input items → `JsonElement` serialization | ❌ Needed |

## Architecture Comparison

### Current: Direct SDK Usage (llm-cli sdk-ask)

```
App
 │
 ▼
ILlmSdkClient.CreateResponseStreamAsync()
 │
 ▼
IAsyncEnumerable<ResponseStreamEvent>
 │
 ▼
App processes events directly (one-shot, no tool loop)
```

### Target: Agent Loop

```
App
 │
 ├── defines IAgentTool[] (schema + execute)
 │
 ▼
AgentLoop.RunAsync(client, prompt, tools, options)
 │
 ├── builds CreateResponseRequest with context
 ├── calls client.CreateResponseStreamAsync()
 ├── emits AgentEvent (message_start, message_update, message_end)
 ├── extracts ResponseFunctionCallItem from completed response
 ├── executes matching IAgentTool
 ├── appends function_call_output to context
 ├── emits AgentEvent (tool_execution_start, tool_execution_end)
 └── repeats until no tool calls
 │
 ▼
IAsyncEnumerable<AgentEvent> (consumed by UI/app)
```

## Pattern Mapping

### 1. pi-agent-core → llm-agent

| pi-agent-core (TS) | llm-agent (C#) | Notes |
|---------------------|-----------------|-------|
| `AgentTool extends Tool` | `IAgentTool` (has `ResponseFunctionToolDefinition` properties + `ExecuteAsync`) | C# uses interface, not inheritance from sealed class |
| `AgentToolResult { content, details }` | `AgentToolResult { Content, Details }` | Same shape |
| `AgentEvent` (union of 10 types) | `AgentEvent` (abstract record + 10 derived records) | C# discriminated union via record hierarchy |
| `agentLoop()` → `EventStream<AgentEvent>` | `AgentLoop.RunAsync()` → `IAsyncEnumerable<AgentEvent>` | C# async enumerable is the natural equivalent |
| `streamSimple(model, context, options)` | `ILlmSdkClient.CreateResponseStreamAsync(request)` | Different shape: pi-ai takes model+context, SDK takes full request |
| `AgentContext { systemPrompt, messages, tools }` | `AgentLoopOptions { Model, Instructions, Tools }` + context list | Flattened into options + managed context |
| `convertToLlm(AgentMessage[] → Message[])` | Direct serialization of `ResponseItem[]` → `JsonElement` | Responses API is already the "LLM format" — no conversion needed |
| `AbortSignal` | `CancellationToken` | Standard .NET cancellation |

### 2. Context Management

**pi-agent-core:** Maintains `AgentMessage[]` (custom union), converts to `Message[]` via `convertToLlm` before each LLM call.

**llm-agent:** Maintains `List<JsonElement>` (serialized response items). The Responses API input IS the LLM format — no conversion step needed. Input items are:
- `{ "type": "message", "role": "user", "content": [...] }` — user messages
- `{ "type": "function_call", "id": "...", "call_id": "...", "name": "...", "arguments": "..." }` — from response output
- `{ "type": "function_call_output", "id": "...", "call_id": "...", "output": "..." }` — tool results

This is simpler than pi-agent-core because we don't need a `convertToLlm` step.

### 3. Fake Pattern

**Existing:** `FakeLlmSdkClient` uses constructor delegates:
```csharp
new FakeLlmSdkClient(
    createResponseStreamAsync: (request, ct) => StreamOf(events...))
```

**Agent tests:** Same pattern — provide canned `ResponseStreamEvent` sequences that include tool calls, then verify the loop executes tools and feeds results back.

## What Exists vs What's Needed

### Currently Built

| Component | Location | Status |
|-----------|----------|--------|
| `ILlmSdkClient` | `src/llm-sdk/Client/` | ✅ Full streaming API |
| `ResponseStreamEvent` hierarchy | `src/llm-sdk/Client/` | ✅ 20+ event types parsed |
| `ResponseFunctionCallItem` | `src/llm-sdk/Core/Models/` | ✅ Tool call in response output |
| `ResponseFunctionCallOutputItem` | `src/llm-sdk/Core/Models/` | ✅ Tool result for input |
| `ResponseFunctionToolDefinition` | `src/llm-sdk/Core/Models/` | ✅ Tool schema declaration |
| `CreateResponseRequest` | `src/llm-sdk/Core/Models/` | ✅ Includes Tools and Input |
| `FakeLlmSdkClient` | `tests/llm-cli.Tests/Fakes/` | ✅ Delegate-based faking |
| `Response` with polymorphic `Output` | `src/llm-sdk/Core/Models/` | ✅ Deserializes function_call items |

### Needed

| Component | Location | Purpose |
|-----------|----------|---------|
| `IAgentTool` | `src/llm-agent/AgentTypes.cs` | Tool schema + execute |
| `AgentEvent` hierarchy | `src/llm-agent/AgentTypes.cs` | 10 lifecycle event types |
| `AgentLoopOptions` | `src/llm-agent/AgentTypes.cs` | Loop configuration |
| `AgentToolResult` | `src/llm-agent/AgentTypes.cs` | Tool execution result |
| `AgentLoop` | `src/llm-agent/AgentLoop.cs` | The loop itself |
| `llm-agent.csproj` | `src/llm-agent/` | Project with ref to llm-sdk |
| `llm-agent.Tests.csproj` | `tests/llm-agent.Tests/` | Test project |
| `FakeLlmSdkClient` (copy) | `tests/llm-agent.Tests/Fakes/` | Test double |

## Key Insights

### What Works Well

- The Responses API is already tool-loop-native — `ResponseFunctionCallItem` contains name, call_id, and arguments; `ResponseFunctionCallOutputItem` feeds results back. No translation layer needed.
- `ILlmSdkClient` is already an abstraction — no additional ports needed. The agent depends on the SDK's public surface.
- The `FakeLlmSdkClient` delegate pattern works perfectly for agent tests — provide canned response sequences.

### Gaps

- No existing context management for multi-turn conversations. The SDK is stateless per-request. The agent must own the growing context array.
- `CreateResponseRequest.Input` is `JsonElement` — we need a clean way to build input arrays from typed items. The existing `ResponseItem` types have JSON attributes, so serializing them works, but we need to handle the initial user message (which is just a string or a message item, not a `ResponseItem`).
- The `ResponseFunctionCallItem` arguments are a raw JSON string. The agent needs to deserialize these per-tool for validation/execution.
