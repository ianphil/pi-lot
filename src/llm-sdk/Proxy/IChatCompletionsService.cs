using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

/// <summary>
/// Port for sending raw Chat Completions requests.
/// </summary>
public interface IChatCompletionsService
{
    /// <summary>
    /// Sends a raw Chat Completions request and returns an HTTP-shaped result.
    /// </summary>
    Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
