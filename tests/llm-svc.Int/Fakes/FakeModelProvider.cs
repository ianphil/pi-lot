using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSvc.Int.Fakes;

internal sealed class FakeModelProvider : IAuthProvider, IModelProvider
{
    public bool IsAuthenticated { get; init; } = true;

    public ModelInfo[] Models { get; init; } = [];

    public ProxyHttpResult ChatCompletionsResult { get; init; } = new("{}", 200);

    public ProxyHttpResult ResponsesResult { get; init; } = new("{}", 200);

    public ChatCompletionRequest? LastChatRequest { get; private set; }

    public CreateResponseRequest? LastResponsesRequest { get; private set; }

    public bool TryLoadCredential() => IsAuthenticated;

    public Task<bool> ValidateTokenAsync() => Task.FromResult(IsAuthenticated);

    public Task<ModelInfo[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);

    public Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        SendChatCompletionsAsync(request, cancellationToken);

    public Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastChatRequest = request;
        return Task.FromResult(ChatCompletionsResult);
    }

    public Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        LastResponsesRequest = request;
        return Task.FromResult(ResponsesResult);
    }

    public Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
