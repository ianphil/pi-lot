using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Ports;

public interface IChatCompletionsService
{
    Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
