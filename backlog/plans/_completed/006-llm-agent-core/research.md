# 006 — llm-agent-core: Research

## Date & Scope

**Date:** 2026-04-06
**Scope:** Design validation for agent loop package, Responses API tool-calling protocol, reference implementation analysis.

## Summary

The design is validated against two sources: the OpenAI Responses API tool-calling protocol (which defines how tools are declared, called, and responded to) and the pi-agent-core reference implementation (which defines the orchestration patterns we're adapting).

## Reference Implementation

### Source

- **Repository:** `~/src/pi-mono/packages/agent` (`@mariozechner/pi-agent-core`)
- **Product spec:** `~/src/macgyver/expertise/badlogic/pi-agent-core.md`
- **Package structure:** 5 files — `types.ts`, `agent-loop.ts`, `agent.ts`, `proxy.ts`, `index.ts`
- **Single dependency:** `@mariozechner/pi-ai`

### Key Design Decisions We're Adopting

| Decision | pi-agent-core | Our adaptation |
|----------|---------------|----------------|
| Loop as a function | `agentLoop()` returns `EventStream<AgentEvent>` | `AgentLoop.RunAsync()` returns `IAsyncEnumerable<AgentEvent>` |
| Tool extends schema | `AgentTool extends Tool` with `execute` function | `IAgentTool` interface with tool definition properties + `ExecuteAsync` |
| 10 lifecycle events | Union type with type discriminator | Abstract record hierarchy with pattern matching |
| Sequential-first execution | Default is parallel, sequential is option | V1 is sequential only |
| Error as tool result | Exceptions caught, returned as `isError: true` | Same — catch, serialize as error output |
| Stateless loop + stateful wrapper | `agentLoop()` is stateless; `Agent` class wraps it | V1 is stateless loop only |

### Decisions We're Deferring

| Feature | Reason |
|---------|--------|
| Parallel tool execution | Added complexity; sequential is correct first |
| Steering/follow-up queues | Requires stateful Agent wrapper |
| Before/after tool hooks | V1 focuses on the core loop |
| `transformContext` | Context windowing is a V2 concern |
| Custom message types | The Responses API input items are sufficient for V1 |
| Proxy stream function | SDK client is the primary path |

## Responses API Tool-Calling Protocol

### Tool Declaration

Tools are declared in `CreateResponseRequest.Tools` as `ResponseFunctionToolDefinition[]`:

```json
{
  "type": "function",
  "name": "read_file",
  "description": "Read a file from disk",
  "parameters": { "type": "object", "properties": { "path": { "type": "string" } }, "required": ["path"] },
  "strict": true
}
```

### Tool Call in Response

When the model wants to call a tool, the response `Output` contains a `ResponseFunctionCallItem`:

```json
{
  "type": "function_call",
  "id": "fc_abc123",
  "call_id": "call_abc123",
  "name": "read_file",
  "arguments": "{\"path\":\"/tmp/test.txt\"}",
  "status": "completed"
}
```

### Tool Result in Next Request

The tool result is fed back as a `ResponseFunctionCallOutputItem` in the next request's `Input` array:

```json
{
  "type": "function_call_output",
  "call_id": "call_abc123",
  "output": "file contents here"
}
```

### Multi-Turn Input Array

For subsequent turns, `Input` is a JSON array containing the previous response's output items plus new tool results:

```json
[
  { "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "fix the bug" }] },
  { "type": "message", "role": "assistant", "content": [{ "type": "output_text", "text": "I'll read the file." }] },
  { "type": "function_call", "id": "fc_1", "call_id": "call_1", "name": "read_file", "arguments": "{\"path\":\"bug.py\"}" },
  { "type": "function_call_output", "call_id": "call_1", "output": "def broken():\n  return None" },
  { "type": "message", "role": "assistant", "content": [{ "type": "output_text", "text": "Found the issue." }] }
]
```

### Stream Events for Tool Calls

During streaming, tool calls arrive as:

1. `response.output_item.added` — `ResponseFunctionCallItem` with empty arguments
2. `response.function_call_arguments.delta` — argument string chunks
3. `response.function_call_arguments.done` — complete arguments string
4. `response.output_item.done` — completed item
5. `response.completed` — full response with all output items

The agent loop processes the **completed response**, not individual deltas — it needs the full `ResponseFunctionCallItem` with complete arguments.

## Open Questions — Resolution

| # | Question | Resolution |
|---|----------|------------|
| Q1 | `IProgress<T>` for tool streaming updates? | Defer to V2 — sequential execution doesn't benefit much |
| Q2 | Typed input items vs raw `JsonElement`? | Use existing `ResponseItem` types for serialization; wrap in helper for building input arrays |
| Q3 | Should `IAgentTool` inherit from `ResponseFunctionToolDefinition`? | No — interface composition. `IAgentTool` has a `ToToolDefinition()` method or the loop extracts the definition |
| Q4 | Where does `FakeLlmSdkClient` live? | Copy into `tests/llm-agent.Tests/Fakes/` — agent tests should be independent |
| Q5 | Namespace for agent package? | `LlmAgent` root namespace, matching `LlmSdk` pattern |

## Conclusion

No blockers. The Responses API tool-calling protocol maps directly to the agent loop's needs. The reference implementation provides a proven pattern. The main adaptation is simplification: the Responses API input IS the LLM format, so we skip the `convertToLlm` step that pi-agent-core needs.
