using System.Runtime.CompilerServices;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Fakes;

internal sealed class FauxLlmSdkClient : ILlmSdkClient
{
    private readonly List<Context> _recordedRequests = [];
    private int _callCount;

    public FauxLlmSdkClient(IEnumerable<FauxResponse> scriptedResponses)
    {
        ArgumentNullException.ThrowIfNull(scriptedResponses);
        Responses = new Queue<FauxResponse>(scriptedResponses);
    }

    public Queue<FauxResponse> Responses { get; }

    public IReadOnlyList<Context> RecordedRequests => _recordedRequests;

    public Task<AssistantMessage> CompleteAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var response = DequeueResponse();
        _recordedRequests.Add(context);
        return Task.FromResult(response.ToAssistantMessage());
    }

    public async IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = DequeueResponse();
        _recordedRequests.Add(context);

        foreach (var streamEvent in response.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.PerEventDelay is not null)
            {
                await Task.Delay(response.PerEventDelay.Value, cancellationToken);
            }

            yield return streamEvent;
        }
    }

    public Task<Response> CreateResponseAsync(CreateResponseRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public Task<Response> CreateResponseAsync(string? model, string input, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        CreateResponseRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        string? model,
        string input,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.");

    public Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OpenAIModelInfo>>([]);

    private FauxResponse DequeueResponse()
    {
        _callCount++;
        if (Responses.TryDequeue(out var response))
        {
            return response;
        }

        throw new InvalidOperationException($"FauxLlmSdkClient has no scripted response for call {_callCount}.");
    }
}
