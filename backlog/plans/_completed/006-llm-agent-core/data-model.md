# 006 — llm-agent-core: Data Model

## Entities

### IAgentTool

The executable tool interface. Composes (not extends) SDK tool schema with execution logic.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | string | Yes | Tool name (matches model's tool call) |
| Description | string | No | Human-readable description for the model |
| Parameters | JsonElement? | No | JSON Schema for arguments |
| Strict | bool? | No | Whether to enforce strict schema |
| ExecuteAsync | function | Yes | Receives parsed `JsonElement` args, returns result |

### AgentToolResult

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Content | string | Yes | — | Text content returned to the model |
| IsError | bool | No | false | Whether this result represents an error |

### AgentLoopOptions

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Model | string | Yes | — | Model identifier |
| Instructions | string | No | null | System prompt via `CreateResponseRequest.Instructions` |
| Tools | IAgentTool[] | No | [] | Available tools |
| MaxTurns | int? | No | null | Maximum turns before stopping (safety limit) |
| Temperature | double? | No | null | Sampling temperature |
| Reasoning | ResponseReasoning? | No | null | Reasoning/thinking configuration |

### AgentEvent (Discriminated Union)

9 event types organized in three layers:

**Agent Lifecycle**

| Event | Fields | Description |
|-------|--------|-------------|
| `AgentStarted` | — | Agent loop has begun |
| `AgentEnded` | `AgentContext Context` | Agent loop has completed; carries typed context |

**Turn Lifecycle**

| Event | Fields | Description |
|-------|--------|-------------|
| `TurnStarted` | — | New LLM call beginning |
| `TurnEnded` | `Response Response, List<AgentToolCallResult> ToolResults` | LLM response + tool results for this turn |

**Message Lifecycle**

| Event | Fields | Description |
|-------|--------|-------------|
| `MessageStarted` | — | Assistant message streaming has begun |
| `MessageDelta` | `ResponseStreamEvent StreamEvent` | Raw stream event (text delta, function call delta, etc.) |
| `MessageEnded` | `Response Response` | Completed response with full output |

**Tool Lifecycle**

| Event | Fields | Description |
|-------|--------|-------------|
| `ToolExecutionStarted` | `string CallId, string ToolName, string Arguments` | Tool execution beginning |
| `ToolExecutionEnded` | `string CallId, string ToolName, AgentToolResult Result` | Tool execution completed (IsError on result covers tool-not-found) |

### AgentToolCallResult

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CallId | string | Yes | Matches `ResponseFunctionCallItem.CallId` |
| ToolName | string | Yes | Tool name |
| Output | string | Yes | Result content |
| IsError | bool | Yes | Whether execution failed |

### AgentContext

Typed context model — serialized to `JsonElement` only at the `CreateResponseRequest.Input` boundary.

| Field | Type | Description |
|-------|------|-------------|
| Items | `IReadOnlyList<AgentContextItem>` | Ordered context items |

### AgentContextItem (Discriminated Union)

| Subtype | Fields | Serializes To |
|---------|--------|---------------|
| `UserMessageContextItem` | `string Text` | `{ "type": "message", "role": "user", "content": [...] }` |
| `ResponseOutputContextItem` | `ResponseItem Item` | Polymorphic `ResponseItem` JSON |
| `ToolResultContextItem` | `string CallId, string Output` | `{ "type": "function_call_output", "call_id": "...", "output": "..." }` |

## Data Flow

### Single Turn (No Tool Calls)

```
AgentLoop.RunAsync(client, "Hello!", options)
    │
    ▼
emit AgentStarted
    │
    ▼
emit TurnStarted
    │
    ▼
Build CreateResponseRequest:
  Model = options.Model
  Instructions = options.Instructions
  Input = context.SerializeInput()  // typed items → JsonElement
  Tools = options.Tools.Select(t => t.ToToolDefinition())
  Stream = true
    │
    ▼
client.CreateResponseStreamAsync(request)
    │
    ├── emit MessageStarted
    ├── emit MessageDelta (per ResponseStreamEvent)
    └── emit MessageEnded (on ResponseCompletedEvent or ResponseIncompleteEvent)
    │
    ▼
No function_call items in Response.Output
    │
    ▼
emit TurnEnded
    │
    ▼
emit AgentEnded (with typed AgentContext)
```

### Multi-Turn (With Tool Calls)

```
AgentLoop.RunAsync(client, "Read test.txt", options)
    │
    ▼
emit AgentStarted
    │
    ▼
TURN 1:
  emit TurnStarted
  Build request with Input = "Read test.txt"
  Stream response → emit MessageStarted/Delta/Ended
  Response.Output contains:
    - ResponseMessageItem ("I'll read that file")
    - ResponseFunctionCallItem (name: "read_file", call_id: "call_1", arguments: '{"path":"test.txt"}')
  │
  Execute tools:
    emit ToolExecutionStarted (call_id: "call_1", name: "read_file")
    Call IAgentTool.ExecuteAsync("call_1", arguments, ct)
    emit ToolExecutionEnded (call_id: "call_1", result: "file contents")
  │
  Append to context:
    - All output items from Response.Output (message + function_call)
    - ResponseFunctionCallOutputItem (call_id: "call_1", output: "file contents")
  │
  emit TurnEnded
    │
    ▼
TURN 2:
  emit TurnStarted
  Build request with Input = serialize(full context array)
  Stream response → emit MessageStarted/Delta/Ended
  No function_call items in output
  emit TurnEnded
    │
    ▼
emit AgentEnded (with final context)
```

### Context Growth

```
Turn 1 Context:
  "Read test.txt"

Turn 2 Context (after tool execution):
  [
    { type: "message", role: "user", content: [{ type: "input_text", text: "Read test.txt" }] },
    { type: "message", role: "assistant", content: [{ type: "output_text", text: "I'll read..." }] },
    { type: "function_call", id: "fc_1", call_id: "call_1", name: "read_file", arguments: "..." },
    { type: "function_call_output", call_id: "call_1", output: "file contents" }
  ]

Turn 3 Context (if more tool calls):
  [ ...all previous items..., ...new output items..., ...new tool results... ]
```

## Validation Summary

| Entity | Rule | Error Handling |
|--------|------|----------------|
| IAgentTool.Name | Must be non-null/empty | ArgumentException at registration |
| Tool call arguments | Must be valid JSON string | Catch `JsonException`, return error result to model |
| Tool name lookup | Must match registered tool | Emit `ToolNotFound` event, return error result to model |
| MaxTurns | If set, loop stops after N turns | Emit `AgentEnded` normally |
| CancellationToken | Checked before each turn and tool execution | Emit `AgentEnded`, stop loop |
