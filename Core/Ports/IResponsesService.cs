using LlmSvc.Core.Models;

namespace LlmSvc.Core.Ports;

public interface IResponsesService
{
    Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}
