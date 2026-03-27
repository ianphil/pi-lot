using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;

namespace llm_svc.Tests.Fakes;

public sealed class FakeModelProvider : IAuthProvider, IModelProvider
{
    public bool IsAuthenticated { get; set; } = true;

    public bool TryLoadCredential() => true;

    public Task<bool> ValidateTokenAsync() => Task.FromResult(IsAuthenticated);

    public ModelDescriptor[] Models { get; set; } = [];

    public ProxyHttpResult ChatCompletionsResult { get; set; } = new("{}", 200);

    public ProxyHttpResult ResponsesResult { get; set; } = new("{}", 200);

    public ProxyHttpResult ChatResult { get; set; } = new("{}", 200);

    public ProxyStreamResult ChatCompletionsStreamResult { get; set; } = new(null, 200, "text/event-stream", EmptyChunks());

    public ProxyStreamResult ResponsesStreamResult { get; set; } = new(null, 200, "text/event-stream", EmptyChunks());

    public ChatCompletionRequest? LastChatRequest { get; private set; }

    public CreateResponseRequest? LastResponsesRequest { get; private set; }

    public Task<ModelDescriptor[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);

    public Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastChatRequest = request;
        return Task.FromResult(ChatResult);
    }

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

    public Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastChatRequest = request;
        return Task.FromResult(ChatCompletionsStreamResult);
    }

    public Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        LastResponsesRequest = request;
        return Task.FromResult(ResponsesStreamResult);
    }

    private static async IAsyncEnumerable<string> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }
}
