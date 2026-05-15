using System.Runtime.CompilerServices;
using LlmSdk.Core.Models;

namespace LlmSdk.Client;

public interface ILlmSdkClient
{
    Task<Response> CreateResponseAsync(
        CreateResponseRequest request,
        CancellationToken cancellationToken = default);

    Task<Response> CreateResponseAsync(
        string? model,
        string input,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        string? model,
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task<ChatCompletionResponse> CreateChatCompletionAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default);

    Task<AssistantMessage> CompleteAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        LlmSdkClientContextAdapter.CompleteAsync(this, context, options, cancellationToken);

    IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        LlmSdkClientContextAdapter.StreamAsync(this, context, options, cancellationToken);

    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        string? model,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);
}
