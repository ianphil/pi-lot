using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSdk.Int.Fakes;

internal sealed class FakeModelProvider : IModelProvider
{
    public ModelInfo[] Models { get; init; } = [];

    public Queue<ProxyHttpResult> ResponsesResults { get; } = [];

    public Queue<ProxyStreamResult> ResponsesStreamResults { get; } = [];

    public List<CreateResponseRequest> ResponsesRequests { get; } = [];

    public List<CreateResponseRequest> ResponsesStreamRequests { get; } = [];

    public Task<ModelInfo[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);

    public Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        ResponsesRequests.Add(request);
        return Task.FromResult(ResponsesResults.Dequeue());
    }

    public Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        ResponsesStreamRequests.Add(request);
        return Task.FromResult(ResponsesStreamResults.Dequeue());
    }
}
