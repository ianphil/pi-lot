using CopilotLlm.Core.Models;

namespace CopilotLlm.Proxy;

public interface IChatCompletionsService
{
    Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
