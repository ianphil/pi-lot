using LlmSvc.Core.Models;

namespace LlmSvc.Core.Ports;

public interface IChatCompletionsService
{
    Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
