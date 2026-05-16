using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

public interface IModelProvider
{
    Task<ModelInfo[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
    Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}

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

    public string Body { get; init; }

    public int StatusCode { get; init; }

    public string ContentType { get; init; }

    public IReadOnlyDictionary<string, string[]> Headers { get; init; }
}

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

    public string? Body { get; init; }

    public int StatusCode { get; init; }

    public string ContentType { get; init; }

    public IAsyncEnumerable<string>? Chunks { get; init; }

    public IReadOnlyDictionary<string, string[]> Headers { get; init; }
}
