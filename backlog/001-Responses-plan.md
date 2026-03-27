# Responses API Implementation Plan (Issue #2)

## Goal
Expose a fully spec-compliant `/v1/responses` endpoint (per [Open Responses spec](https://www.openresponses.org/specification)) so any OpenAI SDK client can target the proxy. All 18 models, including those that only speak `/chat/completions`, appear as Responses API models via transparent translation.

## Current progress

- Completed so far:
  - xUnit test project added and wired into the solution
  - Hexagonal core started with `IModelProvider`, `IResponsesService`, `ResponsesService`, `ChatCompletionsTranslator`, and `ResponseSseSerializer`
  - `CopilotClient` now participates as the outbound provider
  - `POST /v1/responses` added to the app
  - Non-streaming translation works for both native `/responses` models and chat-completions-only models
  - README updated to document `/v1/responses`
- Passing tests currently cover:
  - chat completions -> responses translation
  - basic SSE response shape for streaming requests
  - tool call mapping into `function_call` items
  - structured `model_not_found` errors
  - end-to-end HTTP coverage for `POST /v1/responses`
- Current focus:
  - finish true streaming behavior end-to-end instead of only serializing buffered output into SSE
  - expand state-machine and error-path coverage
  - complete the remaining tool-calling round-trip behavior

## Architecture: Hexagonal (Ports and Adapters)

```
Client (OpenAI SDK)
    ->
[Inbound Adapter]   Program.cs - /v1/responses endpoint
    ->
[Inbound Port]      IResponsesService
    ->
[Core]              ResponsesService - orchestration, translation, model routing
    ->
[Outbound Port]     IModelProvider
    ->
[Outbound Adapter]  CopilotProvider (refactored from CopilotClient)
    ->
Copilot API
```

### Project structure
```
llm-svc\
|- Core\
|  |- Ports\
|  |  |- IResponsesService.cs        (inbound port)
|  |  \- IModelProvider.cs           (outbound port)
|  |- Models\
|  |  |- ResponsesApiModels.cs       (spec-compliant request/response types)
|  \- Services\
|     |- ResponsesService.cs         (orchestration + translation)
|     |- ChatCompletionsTranslator.cs (responses <-> chat completions mapping)
|     \- ResponseSseSerializer.cs    (current SSE serializer)
|- Program.cs                        (composition root / DI wiring)
|- CopilotClient.cs                  (current outbound provider implementation)
\- ...existing files...
```

Current implementation note: the adapter split is only partially extracted so far. `Program.cs` still hosts the inbound HTTP adapter and `CopilotClient.cs` remains the concrete outbound adapter. Separate `Adapters\` folders can still be introduced later if the project grows.

### Test structure
```
llm-svc.Tests\
|- Unit\
|  \- ResponsesServiceTests.cs
|- Integration\
|  \- ResponsesEndpointTests.cs      (WebApplicationFactory)
\- Fakes\
   \- FakeModelProvider.cs           (mock outbound adapter)
```

## Approach: TDD - Red/Green/Refactor

Every feature starts with a failing test. Tests target the core through inbound ports with mocked outbound adapters. Integration tests use `WebApplicationFactory<Program>` for full HTTP pipeline validation.

## Phases

### Phase 1: Scaffolding - Done
- Create xUnit test project, add to solution
- Create hexagonal folder structure
- Define `IModelProvider` outbound port interface
- Define `IResponsesService` inbound port interface
- Create `FakeModelProvider` for testing
- Wire DI in Program.cs

### Phase 2: Core Domain Models - Done
- Define Responses API request model (`CreateResponseRequest`)
- Define Responses API response model (`Response`) with spec-required fields
- Define item types: `message`, `function_call`, `function_call_output`, `reasoning`
- Define content types: `input_text`, `output_text`
- Define status enum: `in_progress`, `completed`, `failed`, `incomplete`
- Define error model per spec

### Phase 3: Non-Streaming Text Response (TDD) - Done for the first working slice
Tests -> Implementation:
1. **Schema test** - POST `/v1/responses` returns valid Response shape with correct `type`, `id`, `status`, `output[]`
2. **Simple message** - single user message in, assistant message out, correct `output_text` content
3. **Multi-message input** - conversation with system + user messages
4. **Model routing** - request for native `/responses` model passes through; request for chat-completions-only model gets translated
5. **Translation fidelity** - chat completions response correctly mapped to Response shape (`usage`, `finish_reason` -> status, message -> output items)
6. **Model not found** - returns spec-compliant error (`invalid_request_error`, `model_not_found`)
7. **Missing required fields** - returns spec-compliant validation error

### Phase 4: Streaming (TDD) - In progress
Tests -> Implementation:
1. **Event ordering** - assert strict sequence: `response.created` -> `response.in_progress` -> `output_item.added` -> `content_part.added` -> `output_text.delta`(s) -> `output_text.done` -> `content_part.done` -> `output_item.done` -> `response.completed`
2. **SSE format** - `Content-Type: text/event-stream`, `event:` field matches `type` in body, terminal `[DONE]`
3. **Delta content** - text deltas accumulate to final text in `output_text.done`
4. **Native streaming passthrough** - models supporting `/responses` natively stream through
5. **Translated streaming** - chat completions `chat.completion.chunk` deltas -> Responses streaming events
6. **State machine** - response status transitions `in_progress` -> `completed`
7. **Stream error** - error mid-stream emits error event followed by `response.failed`

Current state: a basic SSE serializer is in place and covered by tests, but it still serializes buffered output. True upstream streaming and chunk translation are still outstanding.

### Phase 5: Tool / Function Calling (TDD) - Pending
Tests -> Implementation:
1. **Function tool definition** - `tools[]` with `type: "function"` accepted in request
2. **Function call output item** - model emits `function_call` item with `name`, `call_id`, `arguments`
3. **Function call output round-trip** - client sends `function_call_output` item as input, model continues
4. **tool_choice: auto/required/none** - model behavior changes accordingly
5. **Translation** - chat completions `tool_calls` <-> Responses `function_call` items
6. **Streaming function calls** - `function_call_arguments.delta` events

### Phase 6: Error Handling (TDD) - Pending
Tests -> Implementation:
1. **Error response shape** - `{ error: { message, type, code, param } }`
2. **Error types** - `invalid_request` (400), `not_found` (404), `server_error` (500), `too_many_requests` (429)
3. **Upstream failure** - Copilot API error translated to spec error
4. **Auth failure** - 401 from upstream -> appropriate error

### Phase 7: Integration and Cleanup - Pending
- Wire `CopilotProvider` as the real outbound adapter
- Register `/v1/responses` endpoint alongside existing `/v1/chat/completions`
- Update `/v1/models` to include Responses API capability info
- Preserve existing chat completions endpoint (backward compatibility)
- OpenAI SDK smoke test (manual or scripted)
- Update README

## Test Strategy
- **Unit tests** (Phases 2-6): Test `ResponsesService` directly, mock `IModelProvider`
- **Integration tests** (Phase 7): `WebApplicationFactory<Program>` with mocked HTTP upstream
- **Conformance**: Validate against Open Responses spec acceptance tests when available
- **SDK validation**: Run OpenAI Python/JS SDK against the endpoint

## Key Spec References
- [Open Responses Specification](https://www.openresponses.org/specification)
- Items: polymorphic, state machines, streamable, extensible
- Streaming: semantic events (deltas + state machine), strict ordering
- Errors: structured `{ error: { type, code, param, message } }`
- Tools: function definitions, tool_choice, allowed_tools
