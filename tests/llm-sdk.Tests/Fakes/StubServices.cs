using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Proxy;

namespace LlmSdk.Tests.Fakes;

internal sealed class StubResponsesService(ResponseHttpResult result) : IResponsesService
{
    public CreateResponseRequest? LastRequest { get; private set; }

    public Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}

internal sealed class StubChatCompletionsService(ResponseHttpResult result) : IChatCompletionsService
{
    public ChatCompletionRequest? LastRequest { get; private set; }

    public Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}
