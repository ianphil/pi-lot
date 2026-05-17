using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

/// <summary>
/// Port used by proxy-facing consumers to reach Copilot model, chat, response, and streaming endpoints.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// Fetches the available Copilot models.
    /// </summary>
    Task<ModelInfo[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sends a chat request using the provider's compatibility route.
    /// </summary>
    Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sends a raw Chat Completions request.
    /// </summary>
    Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sends a raw Responses request.
    /// </summary>
    Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Streams raw Chat Completions chunks.
    /// </summary>
    Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Streams raw Responses events.
    /// </summary>
    Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP-shaped proxy response for non-streaming provider calls.
/// </summary>
public sealed record ProxyHttpResult
{
    public ProxyHttpResult(
        string body,
        int statusCode,
        string contentType = "application/json",
        IReadOnlyDictionary<string, string[]>? headers = null)
    {
        Body = body;
        StatusCode = statusCode;
        ContentType = contentType;
        Headers = headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Response body.
    /// </summary>
    public string Body { get; init; }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Response content type.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Response headers keyed case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; init; }
}

/// <summary>
/// HTTP-shaped proxy response for streaming provider calls.
/// </summary>
public sealed record ProxyStreamResult
{
    public ProxyStreamResult(
        string? body,
        int statusCode,
        string contentType = "text/event-stream",
        IAsyncEnumerable<string>? chunks = null,
        IReadOnlyDictionary<string, string[]>? headers = null)
    {
        Body = body;
        StatusCode = statusCode;
        ContentType = contentType;
        Chunks = chunks;
        Headers = headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Error or fallback body when a stream is not available.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Response content type, normally text/event-stream for successful streams.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Stream chunks when a stream is available.
    /// </summary>
    public IAsyncEnumerable<string>? Chunks { get; init; }

    /// <summary>
    /// Response headers keyed case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; init; }
}
