using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Ports;

public interface IModelProvider
{
    Task<ModelDescriptor[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
    Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProxyHttpResult(string Body, int StatusCode, string ContentType = "application/json");

public sealed record ProxyStreamResult(string? Body, int StatusCode, string ContentType = "text/event-stream", IAsyncEnumerable<string>? Chunks = null);
