using System.Runtime.CompilerServices;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Fakes;

internal sealed class FauxLlmSdkClient : ILlmSdkClient
{
    private readonly List<Context> _recordedRequests = [];
    private readonly Dictionary<string, int> _cacheUseBySessionId = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<ModelInfo> _models;
    private int _callCount;

    public FauxLlmSdkClient(IEnumerable<FauxResponse> scriptedResponses, IEnumerable<ModelInfo>? models = null)
    {
        ArgumentNullException.ThrowIfNull(scriptedResponses);
        Responses = new Queue<FauxResponse>(scriptedResponses);
        _models = models?.ToArray() ?? [];
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
        return Task.FromResult(ApplyCacheUsage(response.ToAssistantMessage(), options));
    }

    public async IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = DequeueResponse();
        _recordedRequests.Add(context);
        var cacheAdjustedMessage = ApplyCacheUsage(response.ToAssistantMessage(), options);

        foreach (var streamEvent in response.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.PerEventDelay is not null)
            {
                await Task.Delay(response.PerEventDelay.Value, cancellationToken);
            }

            yield return streamEvent switch
            {
                StreamDone => new StreamDone(cacheAdjustedMessage),
                UsageEvent when cacheAdjustedMessage.Usage is not null => new UsageEvent(cacheAdjustedMessage.Usage),
                _ => streamEvent,
            };
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

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_models);
    }

    public Task<ModelInfo> GetModelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? ModelInfo.Unknown(id));
    }

    private FauxResponse DequeueResponse()
    {
        _callCount++;
        if (Responses.TryDequeue(out var response))
        {
            return response;
        }

        throw new InvalidOperationException($"FauxLlmSdkClient has no scripted response for call {_callCount}.");
    }

    private AssistantMessage ApplyCacheUsage(AssistantMessage message, CompletionOptions? options)
    {
        if (options?.Cache is CacheRetention.None or null ||
            string.IsNullOrWhiteSpace(options.SessionId) ||
            message.Usage is null)
        {
            return message;
        }

        var useCount = _cacheUseBySessionId.GetValueOrDefault(options.SessionId);
        _cacheUseBySessionId[options.SessionId] = useCount + 1;
        if (useCount == 0)
        {
            return message;
        }

        var cacheReadTokens = message.Usage.CacheReadTokens > 0
            ? message.Usage.CacheReadTokens
            : message.Usage.InputTokens;
        return message with
        {
            Usage = message.Usage with { CacheReadTokens = cacheReadTokens },
        };
    }
}
