# Data Model: CopilotLlmClient SDK Surface

## Entities

### CopilotLlmOptions

Configuration for the CopilotLlm library, applied during DI registration.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| DefaultModel | string? | No | null | Model used when request omits model |
| HttpTimeout | TimeSpan | No | 120s | HTTP timeout for upstream requests |

**Invariants:**
- `HttpTimeout` must be positive (> TimeSpan.Zero)
- `DefaultModel` if set must be non-empty

### CopilotLlmClient

Hero client for SDK consumers. Singleton, DI-resolved.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| _responsesService | IResponsesService | Yes | — | Injected via DI |
| _chatService | IChatCompletionsService | Yes | — | Injected via DI |
| _modelListService | ModelListService | Yes | — | Injected via DI |
| _options | CopilotLlmOptions | Yes | — | Injected via DI (IOptions<>) |
| _jsonOptions | JsonSerializerOptions | Yes | Web defaults | For deserialization |

**Relationships:**
- Delegates to `IResponsesService` for Responses API
- Delegates to `IChatCompletionsService` for Chat Completions API
- Delegates to `ModelListService` for model listing

### ResponseStreamEvent (Abstract Base)

Typed discriminated union for Responses API SSE events.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Type | string | Yes | — | Event type discriminator |
| SequenceNumber | int | Yes | — | Monotonically increasing event counter |

**Subtypes:**

| Subtype | Additional Fields | SSE Event |
|---------|-------------------|-----------|
| `ResponseCreated` | `Response Response` | `response.created` |
| `ResponseInProgress` | `Response Response` | `response.in_progress` |
| `ResponseCompleted` | `Response Response` | `response.completed` |
| `ResponseFailed` | `Response Response` | `response.failed` |
| `ResponseIncomplete` | `Response Response` | `response.incomplete` |
| `OutputItemAdded` | `ResponseItem Item`, `int OutputIndex` | `response.output_item.added` |
| `OutputItemDone` | `ResponseItem Item`, `int OutputIndex` | `response.output_item.done` |
| `ContentPartAdded` | `ResponseContentPart Part`, `int OutputIndex`, `int ContentIndex` | `response.content_part.added` |
| `ContentPartDone` | `ResponseContentPart Part`, `int OutputIndex`, `int ContentIndex` | `response.content_part.done` |
| `OutputTextDelta` | `string Delta`, `string? ItemId`, `int OutputIndex`, `int ContentIndex` | `response.output_text.delta` |
| `OutputTextDone` | `string Text`, `string? ItemId`, `int OutputIndex`, `int ContentIndex` | `response.output_text.done` |
| `FunctionCallArgumentsDelta` | `string Delta`, `string? ItemId`, `int OutputIndex` | `response.function_call_arguments.delta` |
| `FunctionCallArgumentsDone` | `string Arguments`, `string? ItemId`, `int OutputIndex` | `response.function_call_arguments.done` |
| `ErrorEvent` | `ResponseError Error` | `error` |

### CopilotLlmException (Exception Hierarchy)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| ErrorCode | string | Yes | — | Machine-readable code (`model_not_found`, etc.) |
| ErrorType | string? | No | null | Error category (`invalid_request_error`, etc.) |
| Param | string? | No | null | Parameter that caused the error |
| StatusCode | int | Yes | — | HTTP status code from upstream |

**Subtypes:**

| Subtype | When Thrown |
|---------|------------|
| `ModelNotFoundException` | 404 with `model_not_found` code |
| `AuthenticationException` | 401 status |
| `RateLimitException` | 429 status; adds `RetryAfter` (TimeSpan?) |

## Data Flow

### Non-Streaming: Client → Service → Response

```
CreateResponseAsync("gpt-5.4-mini", "Hello!")
    │
    ▼
Build CreateResponseRequest:
  Model = "gpt-5.4-mini" (or DefaultModel if null)
  Input = JsonDocument.Parse("\"Hello!\"").RootElement.Clone()
  Stream = false
    │
    ▼
IResponsesService.CreateAsync(request)
    │
    ▼
ResponseHttpResult { Body = "{json}", StatusCode = 200 }
    │
    ├── StatusCode >= 400:
    │   Parse Body as error JSON
    │   Map to CopilotLlmException subtype
    │   throw
    │
    └── StatusCode 2xx:
        JsonSerializer.Deserialize<Response>(Body)
        return Response
```

### Streaming: Client → Service → Typed Events

```
CreateResponseStreamAsync("gpt-5.4-mini", "Hello!")
    │
    ▼
Build CreateResponseRequest:
  Model = "gpt-5.4-mini"
  Input = JsonDocument.Parse("\"Hello!\"").RootElement.Clone()
  Stream = true
    │
    ▼
IResponsesService.CreateAsync(request)
    │
    ▼
ResponseHttpResult { Chunks = IAsyncEnumerable<string> }
    │
    ▼
For each chunk string:
  Parse SSE lines:  "event: {type}\ndata: {json}\n\n"
  Deserialize JSON payload based on event type
  yield ResponseStreamEvent subtype
    │
    ▼
Consumer iterates:
  await foreach (var evt in stream)
    if evt is OutputTextDelta delta → Console.Write(delta.Delta)
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| CopilotLlmOptions | HttpTimeout > TimeSpan.Zero | ArgumentOutOfRangeException |
| CopilotLlmOptions | DefaultModel if set must be non-empty | ArgumentException |
| CopilotLlmClient.CreateResponseAsync | model must not be null/empty (unless DefaultModel set) | ArgumentException |
| CopilotLlmClient.CreateResponseAsync | input must not be null | ArgumentNullException |
| CopilotLlmClient | Non-2xx from service | CopilotLlmException (subtype by status/code) |
