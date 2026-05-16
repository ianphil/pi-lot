using System.Runtime.CompilerServices;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Proxy;
using Microsoft.Extensions.Options;

namespace LlmSdk.Client;

public sealed class LlmSdkClient : ILlmSdkClient
{
    private readonly IResponsesService _responsesService;
    private readonly IChatCompletionsService _chatCompletionsService;
    private readonly ModelListService _modelListService;
    private readonly LlmSdkOptions _options;

    public LlmSdkClient(
        IResponsesService responsesService,
        IChatCompletionsService chatCompletionsService,
        ModelListService modelListService,
        IOptions<LlmSdkOptions> options)
    {
        ArgumentNullException.ThrowIfNull(responsesService);
        ArgumentNullException.ThrowIfNull(chatCompletionsService);
        ArgumentNullException.ThrowIfNull(modelListService);
        ArgumentNullException.ThrowIfNull(options);

        _responsesService = responsesService;
        _chatCompletionsService = chatCompletionsService;
        _modelListService = modelListService;
        _options = options.Value;
    }

    public async Task<Response> CreateResponseAsync(
        CreateResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        var result = await _responsesService.CreateAsync(normalizedRequest, cancellationToken);
        return await DeserializeAsync<Response>(result, cancellationToken);
    }

    public Task<Response> CreateResponseAsync(
        string? model,
        string input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return CreateResponseAsync(new CreateResponseRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(input, JsonDefaults.Web),
        }, cancellationToken);
    }

    public async IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request, stream: true);
        var result = await _responsesService.CreateAsync(normalizedRequest, cancellationToken);
        await EnsureSuccessAsync(result, cancellationToken);

        if (result.Chunks is null)
        {
            throw new InvalidOperationException("The response stream did not contain any chunks.");
        }

        await foreach (var chunk in result.Chunks.WithCancellation(cancellationToken))
        {
            var streamEvent = ResponseStreamEvent.Parse(chunk);
            if (streamEvent is not null)
            {
                yield return streamEvent;
            }
        }
    }

    public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
        string? model,
        string input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return CreateResponseStreamAsync(new CreateResponseRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(input, JsonDefaults.Web),
        }, cancellationToken);
    }

    public async Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        var result = await _chatCompletionsService.CreateAsync(normalizedRequest, cancellationToken);
        return await DeserializeAsync<ChatCompletionResponse>(result, cancellationToken);
    }

    public Task<ChatCompletionResponse> CreateChatCompletionAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = message,
                },
            ],
        }, cancellationToken);
    }

    public Task<AssistantMessage> CompleteAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return LlmSdkClientContextAdapter.CompleteAsync(this, context, options, cancellationToken);
    }

    public IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        Context context,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return LlmSdkClientContextAdapter.StreamAsync(this, context, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request, stream: true);
        var result = await _chatCompletionsService.CreateAsync(normalizedRequest, cancellationToken);
        await EnsureSuccessAsync(result, cancellationToken);

        if (result.Chunks is null)
        {
            throw new InvalidOperationException("The chat completion stream did not contain any chunks.");
        }

        await foreach (var chunk in result.Chunks.WithCancellation(cancellationToken))
        {
            var envelope = SseChunkParser.Parse(chunk);
            if (envelope is null)
            {
                continue;
            }

            if (string.Equals(envelope.Value.Data, "[DONE]", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return JsonSerializer.Deserialize<ChatCompletionChunk>(envelope.Value.Data, JsonDefaults.Web)
                         ?? throw new InvalidOperationException("The chat completion stream chunk could not be deserialized.");
        }
    }

    public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        string? model,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return CreateChatCompletionStreamAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = message,
                },
            ],
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        return _modelListService.ListModelsAsync(cancellationToken);
    }

    public Task<ModelInfo> GetModelAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return _modelListService.GetModelAsync(id, cancellationToken);
    }

    private CreateResponseRequest Normalize(CreateResponseRequest request, bool stream = false)
    {
        return new CreateResponseRequest
        {
            Model = ResolveModel(request.Model),
            Input = request.Input,
            Stream = stream,
            Instructions = request.Instructions,
            MaxOutputTokens = request.MaxOutputTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools,
            ToolChoice = request.ToolChoice,
            PreviousResponseId = request.PreviousResponseId,
            Truncation = request.Truncation,
            ParallelToolCalls = request.ParallelToolCalls,
            Text = request.Text,
            PresencePenalty = request.PresencePenalty,
            FrequencyPenalty = request.FrequencyPenalty,
            TopLogprobs = request.TopLogprobs,
            Store = request.Store,
            Background = request.Background,
            ServiceTier = request.ServiceTier,
            Metadata = request.Metadata,
            MaxToolCalls = request.MaxToolCalls,
            Reasoning = request.Reasoning,
            Headers = request.Headers,
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            TimeoutMs = request.TimeoutMs,
            MaxRetries = request.MaxRetries,
            MaxRetryDelayMs = request.MaxRetryDelayMs,
        };
    }

    private ChatCompletionRequest Normalize(ChatCompletionRequest request, bool stream = false)
    {
        return new ChatCompletionRequest
        {
            Model = ResolveModel(request.Model),
            Messages = request.Messages,
            Stream = stream,
            MaxCompletionTokens = request.MaxCompletionTokens,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools,
            ToolChoice = request.ToolChoice,
            Headers = request.Headers,
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            TimeoutMs = request.TimeoutMs,
            MaxRetries = request.MaxRetries,
            MaxRetryDelayMs = request.MaxRetryDelayMs,
            Metadata = request.Metadata,
        };
    }

    private string ResolveModel(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultModel))
        {
            return _options.DefaultModel;
        }

        throw new ArgumentException("A model must be provided when no default model is configured.", "model");
    }

    private static async Task EnsureSuccessAsync(
        ResponseHttpResult result,
        CancellationToken cancellationToken)
    {
        if (result.StatusCode is >= 200 and < 300)
        {
            return;
        }

        var body = await result.ReadBodyAsync(cancellationToken);
        throw LlmSdkExceptionFactory.Create(result.StatusCode, body);
    }

    private static async Task<T> DeserializeAsync<T>(
        ResponseHttpResult result,
        CancellationToken cancellationToken)
        where T : class
    {
        var body = await result.ReadBodyAsync(cancellationToken);

        if (result.StatusCode is < 200 or >= 300)
        {
            throw LlmSdkExceptionFactory.Create(result.StatusCode, body);
        }

        return JsonSerializer.Deserialize<T>(body, JsonDefaults.Web)
               ?? throw new InvalidOperationException($"The response body could not be deserialized into {typeof(T).Name}.");
    }
}
