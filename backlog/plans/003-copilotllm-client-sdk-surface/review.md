# Code Review: Feature 003 - CopilotLlm Client SDK Surface

## Overall Verdict

**Needs changes before merge.**

The branch is headed in a good direction, but the new `/responses` client surface has several streaming issues that are materially out of line with the OpenResponses spec in `/home/cip/src/openresponses`.

## Review Basis

- Branch reviewed: `feature/copilotllm-client-sdk-surface`
- Feature plan: `backlog/plans/003-copilotllm-client-sdk-surface/`
- Spec reference: `/home/cip/src/openresponses`
- Key spec files consulted:
  - `src/lib/sse-parser.ts`
  - `schema/components/schemas/ErrorStreamingEvent.json`
  - `schema/components/schemas/ResponseQueuedStreamingEvent.json`
  - `schema/components/schemas/ResponseReasoningDeltaStreamingEvent.json`
  - `schema/components/schemas/ResponseRefusalDeltaStreamingEvent.json`
  - `schema/components/schemas/ResponseFunctionCallArgumentsDeltaStreamingEvent.json`
  - `schema/components/schemas/ResponseFunctionCallArgumentsDoneStreamingEvent.json`

## Findings

### 1. High - `ResponseStreamEvent.Parse()` is out of spec for valid `/responses` events

**Files**

- `CopilotLlm/ResponseStreamEvent.cs`
- `CopilotLlm/CopilotLlmClient.cs`
- `CopilotLlm/Core/Services/ResponsesService.cs`

**Why it matters**

`openresponses/src/lib/sse-parser.ts` validates a streaming union that includes more event types than this branch supports. The current parser handles only a subset and throws `InvalidOperationException` for anything else:

- `response.queued`
- `response.refusal.delta`
- `response.refusal.done`
- `response.reasoning.delta`
- `response.reasoning.done`
- `response.reasoning_summary.*`
- `response.output_text.annotation.added`

Because `CopilotLlmClient.CreateResponseStreamAsync()` parses every streamed chunk through `ResponseStreamEvent.Parse()`, a spec-valid upstream stream can fail even when the service is behaving correctly.

**Recommendation**

Implement the full OpenResponses streaming event set, or introduce a tolerant fallback such as `UnknownResponseStreamEvent` instead of throwing on unrecognized events.

### 2. High - Streamed `error` events are parsed with the wrong payload shape

**Files**

- `CopilotLlm/ResponseStreamEvent.cs`

**Why it matters**

The OpenResponses schema defines streamed errors as:

```json
{
  "type": "error",
  "sequence_number": 1,
  "error": { ... }
}
```

But `ResponseStreamEvent.Parse()` expects `message`, `code`, and `param` at the top level. That means streamed failures can lose their real error payload when surfaced through the SDK.

**Recommendation**

Parse `error` as a nested payload (`error: ResponseError`) and add a unit test that round-trips a spec-shaped `error` SSE event.

### 3. Medium - Responses-to-chat streaming can misroute tool-call argument deltas

**Files**

- `CopilotLlm/Core/Services/ResponsesStreamToChatTranslator.cs`

**Why it matters**

The OpenResponses schemas for `response.function_call_arguments.delta` and `response.function_call_arguments.done` include both `item_id` and `output_index`. The translator currently tracks a single rolling `toolCallIndex` and applies later argument deltas to that current index instead of mapping by identity.

With multiple tool calls, especially if events are interleaved, argument deltas can be attached to the wrong `tool_calls[index]` in chat-completions output.

**Recommendation**

Track tool-call state by `item_id` or `output_index` and add a test that exercises two interleaved function calls.

### 4. Medium - `CopilotLlmClient` still leaks lower-level construction details

**Files**

- `CopilotLlm/CopilotLlmClient.cs`

**Why it matters**

The SDK-facing client lives in `CopilotLlm.Client`, but its public constructor requires `IResponsesService`, `IChatCompletionsService`, and concrete `ModelListService`. That weakens the intended separation between the client surface and the lower-level proxy/core layers.

**Recommendation**

If DI-only construction is intended, make construction non-public and register via the composition root. Otherwise, depend only on client-facing abstractions.

## Positive Notes

- `Program.cs` remains thin and respects the composition-root boundary.
- DI wiring stays centralized in `ServiceCollectionExtensions`.
- The SDK surface adds substantial unit coverage around the new client API.
