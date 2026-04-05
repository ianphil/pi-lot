using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

public interface IChatCompletionsService
{
    Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
