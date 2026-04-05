# Chat Completions SSE Streaming

## Goal

Add SSE streaming support to the `/chat/completions` surface, with full translation for models that only support `/responses` upstream. This makes both API surfaces symmetric:

| Capability | `/responses` | `/chat/completions` |
| --- | --- | --- |
| Plain text | ✅ | ✅ |
| SSE streaming | ✅ | ❌ → ✅ |
| Tools | ✅ | ✅ |
| Streaming + tools | ✅ | ❌ → ✅ |
| Translation (opposite API) | ✅ chat→responses | ✅ responses→chat (plain only) |
| Streaming translation | ✅ chat→responses | ❌ → ✅ responses→chat |

## Current State

- `Program.cs` endpoint `ProxyChatCompletionsAsync` calls `provider.ChatAsync()` which **hardcodes `Stream = false`** and always returns `ProxyHttpResult` (string body).
- `CopilotClient.ChatAsync()` contains model-routing logic that belongs in a Core service (it mirrors what `ResponsesService` does for `/responses`).
- Infrastructure already has `StreamChatCompletionsAsync()` and `StreamResponsesAsync()` — the plumbing exists.
- `ChatCompletionsStreamTranslator` translates chat→responses SSE. The reverse (responses→chat SSE) does not exist yet.

## Approach — TDD

### Phase 1: Service layer + native SSE pass-through

1. **Define `IChatCompletionsService`** in `Core/Ports/` (mirror of `IResponsesService`).
2. **Create `ChatCompletionsService`** in `Core/Services/`. Absorb the routing logic currently in `CopilotClient.ChatAsync()` — model lookup, endpoint selection, request/response translation. Add `request.Stream` branching.
3. **Wire in `Program.cs`** — replace `provider.ChatAsync()` with the new service. Return SSE via `ResponseHttpResultAdapter` (same adapter `/responses` uses).
4. Tests:
   - Non-streaming plain text (existing behavior, must not regress)
   - Non-streaming with responses-only model translation (existing behavior)
   - **Streaming pass-through** for a chat-capable model (NEW)
   - **Streaming preference** for dual-endpoint model (NEW)

### Phase 2: Reverse stream translator (responses → chat completions SSE)

1. **Create `ResponsesStreamToChatTranslator`** in `Core/Services/`. Reverse of `ChatCompletionsStreamTranslator`:
   - Input: `IAsyncEnumerable<string>` of Responses SSE events
   - Output: `IAsyncEnumerable<string>` of Chat Completions SSE chunks (`ChatCompletionChunk` format)
   - Must handle: `response.output_text.delta` → content delta, `response.completed` → `finish_reason: stop`, tool calls.
2. **Integrate into `ChatCompletionsService`** — when `stream: true` and model is responses-only, call `StreamResponsesAsync` and pipe through the reverse translator.
3. Tests:
   - **Streaming translation** for responses-only model, plain text (NEW)
   - **Streaming translation** with tool calls (NEW)

### Phase 3: Cleanup

1. **Deprecate or simplify `CopilotClient.ChatAsync()`** — routing logic now lives in `ChatCompletionsService`. `ChatAsync` can be reduced to a simple delegate or removed if the service calls `SendChatCompletionsAsync`/`SendResponsesAsync` directly.
2. Review `FakeModelProvider.ChatAsync()` — tests should now go through the service, not the provider's routing method.

## Design Notes

- `ChatCompletionsService` follows the same pattern as `ResponsesService`: takes `IModelProvider`, does model lookup, branches on stream, calls the right provider method, translates if needed.
- The reverse translator is a pure function (async enumerable in, async enumerable out). No I/O, no state beyond the current stream. Unit-testable in isolation.
- SSE line endings: `\n` only, never `\r\n` (existing convention).
- All JSON uses `JsonSerializerDefaults.Web`.
