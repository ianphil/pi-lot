using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

/// <summary>
/// Port for sending raw Responses API requests.
/// </summary>
public interface IResponsesService
{
    /// <summary>
    /// Sends a raw Responses API request and returns an HTTP-shaped result.
    /// </summary>
    Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default);
}
