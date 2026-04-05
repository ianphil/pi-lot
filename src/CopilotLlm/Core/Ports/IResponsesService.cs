using CopilotLlm.Core.Models;

namespace CopilotLlm.Proxy;

public interface IResponsesService
{
    Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}
