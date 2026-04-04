using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Ports;

public interface IResponsesService
{
    Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}
