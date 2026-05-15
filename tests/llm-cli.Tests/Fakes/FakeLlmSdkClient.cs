using System.Runtime.CompilerServices;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Tests.Fakes;

internal sealed class FakeLlmSdkClient : ILlmSdkClient
{
    private readonly Func<CreateResponseRequest, CancellationToken, Task<Response>>? _createResponseAsync;
    private readonly Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>>? _createResponseStreamAsync;
    private readonly Func<ChatCompletionRequest, CancellationToken, Task<ChatCompletionResponse>>? _createChatCompletionAsync;
    private readonly Func<ChatCompletionRequest, CancellationToken, IAsyncEnumerable<ChatCompletionChunk>>? _createChatCompletionStreamAsync;
    private readonly Func<Context, CompletionOptions?, CancellationToken, Task<AssistantMessage>>? _completeAsync;
    private readonly Func<Context, CompletionOptions?, CancellationToken, IAsyncEnumerable<AssistantStreamEvent>>? _streamAsync;
    private readonly Queue<AssistantMessage>? _completeResponses;
    private readonly Queue<IReadOnlyList<AssistantStreamEvent>>? _streamResponses;

    public FakeLlmSdkClient(
        Func<CreateResponseRequest, CancellationToken, Task<Response>>? createResponseAsync = null,
        Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>>? createResponseStreamAsync = null,
        Func<ChatCompletionRequest, CancellationToken, Task<ChatCompletionResponse>>? createChatCompletionAsync = null,
        Func<ChatCompletionRequest, CancellationToken, IAsyncEnumerable<ChatCompletionChunk>>? createChatCompletionStreamAsync = null,
        Func<Context, CompletionOptions?, CancellationToken, Task<AssistantMessage>>? completeAsync = null,
        Func<Context, CompletionOptions?, CancellationToken, IAsyncEnumerable<AssistantStreamEvent>>? streamAsync = null,
        IEnumerable<AssistantMessage>? completeResponses = null,
        IEnumerable<IReadOnlyList<AssistantStreamEvent>>? streamResponses = null)
    {
        _createResponseAsync = createResponseAsync;
        _createResponseStreamAsync = createResponseStreamAsync;
        _createChatCompletionAsync = createChatCompletionAsync;
        _createChatCompletionStreamAsync = createChatCompletionStreamAsync;
        _completeAsync = completeAsync;
        _streamAsync = streamAsync;
        _completeResponses = completeResponses is null ? null : new Queue<AssistantMessage>(completeResponses);
        _streamResponses = streamResponses is null ? null : new Queue<IReadOnlyList<AssistantStreamEvent>>(streamResponses);
    }

    public static FakeLlmSdkClient WithContextResponses(params AssistantMessage[] responses) =>
        new(completeResponses: responses);

    public static FakeLlmSdkClient WithContextStreams(params IReadOnlyList<AssistantStreamEvent>[] responses) =>
        new(streamResponses: responses);

    public CreateResponseRequest? LastCreateResponseRequest { get; private set; }
    public CreateResponseRequest? LastCreateResponseStreamRequest { get; private set; }
    public ChatCompletionRequest? LastCreateChatCompletionRequest { get; private set; }
    public ChatCompletionRequest? LastCreateChatCompletionStreamRequest { get; private set; }
    public Context? LastCompleteContext { get; private set; }
    public CompletionOptions? LastCompletionOptions { get; private set; }
    public Context? LastStreamContext { get; private set; }
    public CompletionOptions? LastStreamOptions { get; private set; }

    public Task<Response> CreateResponseAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateResponseRequest = request;
        return _createResponseAsync?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public Task<Response> CreateResponseAsync(string? model, string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateResponseStreamRequest = request;
        return _createResponseStreamAsync?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(string? model, string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateChatCompletionRequest = request;
        return _createChatCompletionAsync?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(string? model, string message, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<AssistantMessage> CompleteAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastCompleteContext = context;
        LastCompletionOptions = options;
        if (_completeResponses is not null)
        {
            if (_completeResponses.TryDequeue(out var response))
            {
                return Task.FromResult(response);
            }

            throw new InvalidOperationException("FakeLlmSdkClient has no scripted completion response.");
        }

        return _completeAsync?.Invoke(context, options, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastStreamContext = context;
        LastStreamOptions = options;
        if (_streamResponses is not null)
        {
            if (_streamResponses.TryDequeue(out var response))
            {
                return ToAsyncEnumerable(response, cancellationToken);
            }

            throw new InvalidOperationException("FakeLlmSdkClient has no scripted stream response.");
        }

        return _streamAsync?.Invoke(context, options, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateChatCompletionStreamRequest = request;
        return _createChatCompletionStreamAsync?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(string? model, string message, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static async IAsyncEnumerable<AssistantStreamEvent> ToAsyncEnumerable(
        IEnumerable<AssistantStreamEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var streamEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return streamEvent;
            await Task.Yield();
        }
    }
}
