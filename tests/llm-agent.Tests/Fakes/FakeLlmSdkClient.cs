using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Fakes;

internal sealed class FakeLlmSdkClient : ILlmSdkClient
{
    private readonly Func<CreateResponseRequest, CancellationToken, Task<Response>>? _createResponseAsync;
    private readonly Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>>? _createResponseStreamAsync;
    private readonly Func<ChatCompletionRequest, CancellationToken, Task<ChatCompletionResponse>>? _createChatCompletionAsync;
    private readonly Func<ChatCompletionRequest, CancellationToken, IAsyncEnumerable<ChatCompletionChunk>>? _createChatCompletionStreamAsync;

    public FakeLlmSdkClient(
        Func<CreateResponseRequest, CancellationToken, Task<Response>>? createResponseAsync = null,
        Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>>? createResponseStreamAsync = null,
        Func<ChatCompletionRequest, CancellationToken, Task<ChatCompletionResponse>>? createChatCompletionAsync = null,
        Func<ChatCompletionRequest, CancellationToken, IAsyncEnumerable<ChatCompletionChunk>>? createChatCompletionStreamAsync = null)
    {
        _createResponseAsync = createResponseAsync;
        _createResponseStreamAsync = createResponseStreamAsync;
        _createChatCompletionAsync = createChatCompletionAsync;
        _createChatCompletionStreamAsync = createChatCompletionStreamAsync;
    }

    public CreateResponseRequest? LastCreateResponseRequest { get; private set; }
    public CreateResponseRequest? LastCreateResponseStreamRequest { get; private set; }
    public ChatCompletionRequest? LastCreateChatCompletionRequest { get; private set; }
    public ChatCompletionRequest? LastCreateChatCompletionStreamRequest { get; private set; }
    public IReadOnlyList<ModelInfo> Models { get; init; } = [];

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

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        LastCreateChatCompletionStreamRequest = request;
        return _createChatCompletionStreamAsync?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();
    }

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(string? model, string message, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Models);

    public Task<ModelInfo> GetModelAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) ?? ModelInfo.Unknown(id));
}
