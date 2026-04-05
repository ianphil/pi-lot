using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

public interface IResponsesService
{
    Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}
