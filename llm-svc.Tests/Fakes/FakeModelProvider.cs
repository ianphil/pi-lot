using LlmSvc;
using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;

namespace llm_svc.Tests.Fakes;

public sealed class FakeModelProvider : IModelProvider
{
    public bool IsAuthenticated { get; set; } = true;

    public bool TryLoadCredential() => true;

    public Task<bool> ValidateTokenAsync() => Task.FromResult(IsAuthenticated);

    public ModelDescriptor[] Models { get; set; } = [];

    public ProxyHttpResult ChatCompletionsResult { get; set; } = new("{}", 200);

    public ProxyHttpResult ResponsesResult { get; set; } = new("{}", 200);

    public ChatCompletionRequest? LastChatRequest { get; private set; }

    public CreateResponseRequest? LastResponsesRequest { get; private set; }

    public Task<ModelDescriptor[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);

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
}
