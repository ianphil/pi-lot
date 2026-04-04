# CopilotLlmClient SDK Surface — Analysis

## Executive Summary

| Pattern | Integration Point |
|---------|-------------------|
| Hero client wrapping existing services | `CopilotLlmClient` delegates to `IResponsesService`, `IChatCompletionsService`, `ModelListService` |
| Options-bag configuration | `CopilotLlmOptions` wired through `AddCopilotLlm(Action<>)` overload |
| Typed stream events | New `ResponseStreamEvent` hierarchy parses existing SSE `IAsyncEnumerable<string>` |
| Extension methods for convenience | `ResponseExtensions` adds `GetOutputText()` without violating DTO-purity rule |
| Namespace separation | `CopilotLlm.Client` (SDK) / `CopilotLlm.Proxy` (transport) / `CopilotLlm.Core.Models` (shared) |

## Architecture Comparison

### Current Architecture

```
Consumer code
    │
    ▼
AddCopilotLlm()  →  resolves IResponsesService / IChatCompletionsService
    │
    ▼
service.CreateAsync(CreateResponseRequest { Input = JsonDocument.Parse(...) })
    │
    ▼
ResponseHttpResult { Body, Chunks, StatusCode, ContentType }
    │
    ▼
Consumer deserializes JSON, checks status codes, handles SSE manually
```

**Problems:**
- `JsonElement Input` requires `JsonDocument.Parse("\"Hello\"").RootElement.Clone()` for a string
- `ResponseHttpResult` is HTTP-shaped — status codes, raw JSON bodies, SSE chunks as strings
- No convenience overloads for the 90% case (model + string)
- No configuration hooks (timeouts, default model)
- No typed streaming events — consumers must parse SSE text manually

### Target Architecture

```
SDK consumers                          Proxy hosts (llm-svc)
    │                                      │
    ▼                                      ▼
CopilotLlmClient                     IResponsesService (existing)
    │                                      │
    ├─ CreateResponseAsync(model, input)   ├─ CreateAsync(request)
    ├─ CreateResponseStreamAsync(...)      │
    ├─ CreateChatCompletionAsync(...)      IChatCompletionsService
    ├─ ListModelsAsync()                   │
    │                                      ├─ CreateAsync(request)
    ▼                                      ▼
Response (typed)                      ResponseHttpResult (HTTP-shaped)
IAsyncEnumerable<ResponseStreamEvent> IAsyncEnumerable<string>
throws CopilotLlmException           returns status codes
```

## Pattern Mapping

### 1. DI Registration

**Current Implementation:**
```csharp
public static IServiceCollection AddCopilotLlm(this IServiceCollection services)
// Registers 12+ singletons, no configuration
```

**Target Evolution:**
```csharp
// Existing — preserved
public static IServiceCollection AddCopilotLlm(this IServiceCollection services)

// New overload
public static IServiceCollection AddCopilotLlm(
    this IServiceCollection services,
    Action<CopilotLlmOptions> configure)
```

### 2. Request Creation

**Current Implementation:**
```csharp
var result = await service.CreateAsync(new CreateResponseRequest
{
    Model = "gpt-5.4-mini",
    Input = JsonDocument.Parse("\"Hello!\"").RootElement.Clone(),
});
var body = await result.ReadBodyAsync();
var response = JsonSerializer.Deserialize<Response>(body, ...);
```

**Target Evolution:**
```csharp
var result = await client.CreateResponseAsync("gpt-5.4-mini", "Hello!");
Console.WriteLine(result.GetOutputText());
```

### 3. Streaming

**Current Implementation:**
- `ResponseHttpResult.Chunks` is `IAsyncEnumerable<string>` — raw SSE lines
- Consumers must parse `event:` / `data:` lines and deserialize JSON manually
- 15+ event types exist but only as serialized JSON, no typed models

**Target Evolution:**
- `IAsyncEnumerable<ResponseStreamEvent>` with typed variants
- Each event carries parsed data (text deltas, tool calls, usage)
- Consumers pattern-match on event types

### 4. Error Handling

**Current Implementation:**
- `ResponseHttpResult.StatusCode` carries HTTP status
- Error JSON in `Body` must be deserialized by consumer
- `ResponseApiException` exists but is thrown internally, not surfaced at SDK boundary

**Target Evolution:**
- `CopilotLlmClient` throws `CopilotLlmException` subtypes
- `ModelNotFoundException`, `AuthenticationException`, `RateLimitException`
- Consumers use try/catch, never check status codes

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| `IResponsesService` + `ResponsesService` | ✅ | Full request-object API, streaming + non-streaming |
| `IChatCompletionsService` + `ChatCompletionsService` | ✅ | Full request-object API, streaming + non-streaming |
| `ModelListService` | ✅ | Returns `OpenAIModelListResponse` |
| `ResponseHttpResult` | ✅ | Dual-mode (Body or Chunks), factory methods |
| `ChatCompletionsTranslator` | ✅ | Bidirectional translation |
| `ChatCompletionsStreamTranslator` | ✅ | Chat→Responses stream translation |
| `ResponsesStreamToChatTranslator` | ✅ | Responses→Chat stream translation |
| `ResponseSseSerializer` | ✅ | Complete SSE event generation |
| All model DTOs | ✅ | `Response`, `ChatCompletionResponse`, `ModelDescriptor`, etc. |
| `CopilotClient` auth lifecycle | ✅ | Preemptive refresh + 401 retry (lib-v0.2.0) |
| `FakeModelProvider` | ✅ | Comprehensive test double |

### Needed

| Component | Status | Builds On |
|-----------|--------|-----------|
| `CopilotLlmOptions` | ❌ | New type |
| `AddCopilotLlm(Action<>)` overload | ❌ | Existing `AddCopilotLlm()` |
| `CopilotLlmClient` | ❌ | Wraps `IResponsesService`, `IChatCompletionsService`, `ModelListService` |
| Convenience overloads | ❌ | Builds `CreateResponseRequest` from (model, string) |
| `ResponseStreamEvent` hierarchy | ❌ | Parses existing SSE string chunks |
| `ResponseExtensions.GetOutputText()` | ❌ | Navigates existing `Response` model |
| `CopilotLlmException` hierarchy | ❌ | Wraps `ResponseHttpResult` error status codes |
| Namespace reorganization | ❌ | Moves existing types, updates usings |

## Key Insights

### What Works Well

1. **Core engine is solid** — translation, routing, auth, streaming all work correctly
2. **Model types are comprehensive** — `Response`, `ChatCompletionResponse`, etc. are well-defined
3. **SSE serialization covers all event types** — `ResponseSseSerializer` emits 15+ event types
4. **Test infrastructure exists** — `FakeModelProvider` covers both `IAuthProvider` and `IModelProvider`
5. **Pre-1.0 versioning** — no backwards compatibility tax, can make breaking changes freely

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| `JsonElement Input` is painful for strings | Convenience overloads handle common cases |
| No typed stream events | Parse SSE strings into discriminated union |
| All services are public but transport-shaped | Namespace separation signals intent |
| No configuration mechanism | `CopilotLlmOptions` + configure delegate |
| `ModelListService` not behind an interface | `CopilotLlmClient` wraps it directly |
| Chat completions streaming returns different type than responses | Separate stream event types per API or unified |
