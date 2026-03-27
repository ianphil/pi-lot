using LlmSvc.Core.Models;

namespace LlmSvc.Core.Ports;

public interface IModelProvider
{
    bool IsAuthenticated { get; }
    bool TryLoadCredential();
    Task<bool> ValidateTokenAsync();
    Task<ModelDescriptor[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProxyHttpResult(string Body, int StatusCode, string ContentType = "application/json");
