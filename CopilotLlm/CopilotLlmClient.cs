using System.Text.Json;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using Microsoft.Extensions.Options;

namespace CopilotLlm.Client;

public sealed class CopilotLlmClient
{
    private readonly IResponsesService _responsesService;
    private readonly IChatCompletionsService _chatCompletionsService;
    private readonly ModelListService _modelListService;
    private readonly CopilotLlmOptions _options;

    public CopilotLlmClient(
        IResponsesService responsesService,
        IChatCompletionsService chatCompletionsService,
        ModelListService modelListService,
        IOptions<CopilotLlmOptions> options)
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

    public async Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _modelListService.GetModelsAsync(cancellationToken);
        return response.Data;
    }

    private CreateResponseRequest Normalize(CreateResponseRequest request)
    {
        return new CreateResponseRequest
        {
            Model = ResolveModel(request.Model),
            Input = request.Input,
            Stream = false,
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
        };
    }

    private ChatCompletionRequest Normalize(ChatCompletionRequest request)
    {
        return new ChatCompletionRequest
        {
            Model = ResolveModel(request.Model),
            Messages = request.Messages,
            Stream = false,
            MaxCompletionTokens = request.MaxCompletionTokens,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools,
            ToolChoice = request.ToolChoice,
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

    private static async Task<T> DeserializeAsync<T>(
        ResponseHttpResult result,
        CancellationToken cancellationToken)
        where T : class
    {
        var body = await result.ReadBodyAsync(cancellationToken);

        if (result.StatusCode is < 200 or >= 300)
        {
            throw CopilotLlmExceptionFactory.Create(result.StatusCode, body);
        }

        return JsonSerializer.Deserialize<T>(body, JsonDefaults.Web)
               ?? throw new InvalidOperationException($"The response body could not be deserialized into {typeof(T).Name}.");
    }
}
