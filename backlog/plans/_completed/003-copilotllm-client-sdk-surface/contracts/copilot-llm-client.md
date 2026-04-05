# Contract: CopilotLlmClient

## Purpose

Hero client for SDK consumers. Single entry point for Responses API, Chat Completions API, and model listing. Wraps proxy services with typed results and convenience overloads.

## Public API

```csharp
namespace CopilotLlm.Client;

public sealed class CopilotLlmClient
{
    // ── Responses API (non-streaming) ──────────────────────────────

    /// Canonical: full request object (stable contract)
    Task<Response> CreateResponseAsync(
        CreateResponseRequest request,
        CancellationToken cancellationToken = default);

    /// Convenience: model + string input (hero path)
    Task<Response> CreateResponseAsync(
        string model,
        string input,
        CancellationToken cancellationToken = default);

    // ── Responses API (streaming) ──────────────────────────────────

    /// Canonical: full request object
    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// Convenience: model + string input
    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        string model,
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ── Chat Completions API (non-streaming) ───────────────────────

    /// Canonical: full request object
    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// Convenience: model + single user message
    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        string model,
        string message,
        CancellationToken cancellationToken = default);

    // ── Chat Completions API (streaming) ───────────────────────────

    /// Canonical: full request object
    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// Convenience: model + single user message
    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        string model,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ── Models ─────────────────────────────────────────────────────

    Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);
}
```

## Behavior

### Model Resolution

1. If request has a model → use it
2. If request model is null → use `CopilotLlmOptions.DefaultModel`
3. If both null → throw `ArgumentException`

### Non-Streaming Flow

1. Set `Stream = false` on request (or leave null)
2. Delegate to internal service (`.CreateAsync()`)
3. Check `ResponseHttpResult.StatusCode`:
   - `>= 400`: Parse error JSON, throw appropriate `CopilotLlmException`
   - `2xx`: Deserialize `Body` JSON into typed result
4. Return typed result

### Streaming Flow

1. Set `Stream = true` on request
2. Delegate to internal service
3. Check `ResponseHttpResult.StatusCode`:
   - `>= 400`: Parse error body, throw `CopilotLlmException`
4. Iterate `ResponseHttpResult.Chunks`
5. Parse each SSE chunk into typed `ResponseStreamEvent`
6. Yield parsed events

### Error Mapping

| HTTP Status | Error Code | Exception Type |
|-------------|------------|----------------|
| 401 | any | `AuthenticationException` |
| 404 | `model_not_found` | `ModelNotFoundException` |
| 429 | any | `RateLimitException` |
| other 4xx/5xx | any | `CopilotLlmException` |

## Dependencies

- `IResponsesService` (via DI)
- `IChatCompletionsService` (via DI)
- `ModelListService` (via DI)
- `IOptions<CopilotLlmOptions>` (via DI)

## Extension Methods

```csharp
namespace CopilotLlm.Client;

public static class ResponseExtensions
{
    /// Returns the text content of the first output message, or null.
    public static string? GetOutputText(this Response response);
}

public static class ChatCompletionExtensions
{
    /// Returns the content of the first choice's message, or null.
    public static string? GetMessageText(this ChatCompletionResponse response);
}
```
