using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Int.Fakes;

internal sealed class FakeLlmSdkClient : ILlmSdkClient
{
    private readonly Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>> _createResponseStreamAsync;

    public FakeLlmSdkClient(Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>> createResponseStreamAsync)
    {
        _createResponseStreamAsync = createResponseStreamAsync;
    }

    public List<CreateResponseRequest> CreateResponseStreamRequests { get; } = [];

    public IReadOnlyList<ModelInfo> Models { get; init; } = [];

    public Task<Response> CreateResponseAsync(CreateResponseRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Response> CreateResponseAsync(string? model, string input, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        CreateResponseStreamRequests.Add(request);
        return _createResponseStreamAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(string? model, string input, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(string? model, string message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(string? model, string message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Models);

    public Task<ModelInfo> GetModelAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) ?? ModelInfo.Unknown(id));
}
