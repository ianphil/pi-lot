using System.Runtime.CompilerServices;
using LlmSdk.Core.Models;

namespace LlmSdk.Client;

/// <summary>
/// Main client surface for Copilot-backed Responses, Chat Completions, model discovery, and portable context calls.
/// </summary>
public interface ILlmSdkClient
{
    /// <summary>
    /// Sends a raw Responses API request.
    /// </summary>
    Task<Response> CreateResponseAsync(
        CreateResponseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a simple text prompt through the Responses API.
    /// </summary>
    Task<Response> CreateResponseAsync(
        string? model,
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams raw Responses API events.
    /// </summary>
    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams raw Responses API events for a simple text prompt.
    /// </summary>
    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        string? model,
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a raw Chat Completions request.
    /// </summary>
    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a simple user message through the Chat Completions API.
    /// </summary>
    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a portable context request and returns a unified assistant message.
    /// </summary>
    /// <remarks>
    /// By default, recoverable stream interruptions return partial messages. Set
    /// <see cref="CompletionOptions.AbortMode"/> to <see cref="AbortMode.Throw"/>
    /// to preserve exception behavior after a stream starts.
    /// </remarks>
    Task<AssistantMessage> CompleteAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        LlmSdkClientContextAdapter.CompleteAsync(this, context, options, cancellationToken);

    /// <summary>
    /// Streams a portable context request as unified assistant stream events.
    /// </summary>
    IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        LlmSdkClientContextAdapter.StreamAsync(this, context, options, cancellationToken);

    /// <summary>
    /// Streams raw Chat Completions chunks.
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams raw Chat Completions chunks for a simple user message.
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        string? model,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists Copilot models currently available through the SDK.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for a model id, or an unknown model placeholder when metadata is unavailable.
    /// </summary>
    Task<ModelInfo> GetModelAsync(
        string id,
        CancellationToken cancellationToken = default);
}
