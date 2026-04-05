using System.Net;
using System.Text.Json;
using CopilotLlm.Core.Models;
using CopilotLlm.Proxy;
using static CopilotLlm.Core.Models.JsonElementHelpers;

namespace CopilotLlm.Core.Services;

public sealed class ResponsesService : IResponsesService
{
    private readonly IModelProvider _provider;
    private readonly ChatCompletionsTranslator _translator;
    private readonly ChatCompletionsStreamTranslator _streamTranslator;

    public ResponsesService(IModelProvider provider, ChatCompletionsTranslator translator)
        : this(provider, translator, new ChatCompletionsStreamTranslator())
    {
    }

    public ResponsesService(IModelProvider provider, ChatCompletionsTranslator translator, ChatCompletionsStreamTranslator streamTranslator)
    {
        _provider = provider;
        _translator = translator;
        _streamTranslator = streamTranslator;
    }

    public async Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Validate(request);

            var models = await _provider.FetchModelsAsync(cancellationToken: cancellationToken);
            var model = models.FirstOrDefault(item =>
                string.Equals(item.Id, request.Model, StringComparison.OrdinalIgnoreCase));

            if (model is null)
            {
                throw new ResponseApiException(404, new ResponseError
                {
                    Message = $"The requested model '{request.Model}' does not exist.",
                    Type = ErrorTypes.InvalidRequestError,
                    Param = "model",
                    Code = ErrorCodes.ModelNotFound,
                });
            }

            if (request.Stream == true)
            {
                if (model.SupportsResponses)
                {
                    var upstream = await _provider.StreamResponsesAsync(CloneForStreaming(request), cancellationToken);
                    if (upstream.StatusCode >= 400)
                    {
                        return NormalizeStreamError(upstream);
                    }

                    return ResponseHttpResult.FromStream(
                        NormalizeNativeStreamChunks(upstream.Chunks ?? EmptyChunks()),
                        upstream.StatusCode,
                        upstream.ContentType);
                }

                if (model.SupportsChatCompletions)
                {
                    var completionRequest = _translator.ToChatCompletionRequest(request, stream: true);
                    var upstream = await _provider.StreamChatCompletionsAsync(completionRequest, cancellationToken);
                    if (upstream.StatusCode >= 400)
                    {
                        return NormalizeStreamError(upstream);
                    }

                    return ResponseHttpResult.FromStream(
                        _streamTranslator.TranslateStream(upstream.Chunks ?? EmptyChunks(), request, cancellationToken),
                        200,
                        "text/event-stream");
                }

                throw new ResponseApiException(400, new ResponseError
                {
                    Message = $"Model '{request.Model}' does not support /responses or /chat/completions.",
                    Type = ErrorTypes.InvalidRequestError,
                    Param = "model",
                    Code = ErrorCodes.UnsupportedModelEndpoint,
                });
            }

            Response response;
            if (model.SupportsResponses)
            {
                var upstream = await _provider.SendResponsesAsync(CloneWithoutStreaming(request), cancellationToken);
                if (upstream.StatusCode >= 400)
                {
                    return NormalizeError(upstream);
                }

                response = _translator.NormalizeNativeResponse(upstream.Body, request);
            }
            else if (model.SupportsChatCompletions)
            {
                var completionRequest = _translator.ToChatCompletionRequest(request);
                var upstream = await _provider.SendChatCompletionsAsync(completionRequest, cancellationToken);
                if (upstream.StatusCode >= 400)
                {
                    return NormalizeError(upstream);
                }

                response = _translator.ToResponse(upstream.Body, request);
            }
            else
            {
                throw new ResponseApiException(400, new ResponseError
                {
                    Message = $"Model '{request.Model}' does not support /responses or /chat/completions.",
                    Type = ErrorTypes.InvalidRequestError,
                    Param = "model",
                    Code = ErrorCodes.UnsupportedModelEndpoint,
                });
            }

            return ResponseHttpResult.FromBody(
                JsonSerializer.Serialize(response, JsonDefaults.Web),
                200,
                "application/json");
        }
        catch (ResponseApiException ex)
        {
            return ResponseHttpResult.FromBody(
                JsonSerializer.Serialize(new ResponseErrorEnvelope { Error = ex.Error }, JsonDefaults.Web),
                ex.StatusCode,
                "application/json");
        }
    }

    private static void Validate(CreateResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ResponseApiException(400, new ResponseError
            {
                Message = "model is required",
                Type = ErrorTypes.InvalidRequestError,
                Param = "model",
                Code = ErrorCodes.MissingRequiredParameter,
            });
        }

        if (request.Input.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new ResponseApiException(400, new ResponseError
            {
                Message = "input is required",
                Type = ErrorTypes.InvalidRequestError,
                Param = "input",
                Code = ErrorCodes.MissingRequiredParameter,
            });
        }
    }

    private static CreateResponseRequest CloneWithoutStreaming(CreateResponseRequest request) => new()
    {
        Model = request.Model,
        Input = CloneOrDefault(request.Input),
        Stream = false,
        Instructions = request.Instructions,
        MaxOutputTokens = request.MaxOutputTokens,
        Temperature = request.Temperature,
        TopP = request.TopP,
        Tools = request.Tools,
        ToolChoice = CloneOrNull(request.ToolChoice),
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

    private static CreateResponseRequest CloneForStreaming(CreateResponseRequest request) => new()
    {
        Model = request.Model,
        Input = CloneOrDefault(request.Input),
        Stream = true,
        Instructions = request.Instructions,
        MaxOutputTokens = request.MaxOutputTokens,
        Temperature = request.Temperature,
        TopP = request.TopP,
        Tools = request.Tools,
        ToolChoice = CloneOrNull(request.ToolChoice),
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

    private static ResponseHttpResult NormalizeError(ProxyHttpResult upstream)
    {
        var error = ParseError(upstream.Body, upstream.StatusCode);
        return ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(new ResponseErrorEnvelope { Error = error }, JsonDefaults.Web),
            upstream.StatusCode,
            "application/json");
    }

    private static ResponseHttpResult NormalizeStreamError(ProxyStreamResult upstream)
    {
        var error = ParseError(upstream.Body ?? string.Empty, upstream.StatusCode);
        return ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(new ResponseErrorEnvelope { Error = error }, JsonDefaults.Web),
            upstream.StatusCode,
            "application/json");
    }

    private static ResponseError ParseError(string body, int statusCode)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
            if (envelope?.Error is not null)
            {
                return NormalizeParsedError(envelope.Error, statusCode);
            }

            var openAiError = JsonSerializer.Deserialize<OpenAIErrorResponse>(body, JsonDefaults.Web);
            if (openAiError?.Error is not null)
            {
                return new ResponseError
                {
                    Message = openAiError.Error.Message,
                    Type = NormalizeErrorType(openAiError.Error.Type, statusCode),
                    Code = openAiError.Error.Code,
                };
            }
        }
        catch (JsonException)
        {
        }

        return new ResponseError
        {
            Message = string.IsNullOrWhiteSpace(body) ? "Upstream request failed." : body,
            Type = MapErrorType(statusCode),
        };
    }

    private static ResponseError NormalizeParsedError(ResponseError error, int statusCode) => new()
    {
        Message = error.Message,
        Type = NormalizeErrorType(error.Type, statusCode),
        Param = error.Param,
        Code = error.Code,
    };

    private static string NormalizeErrorType(string? errorType, int statusCode) =>
        string.IsNullOrWhiteSpace(errorType) || string.Equals(errorType, "error", StringComparison.OrdinalIgnoreCase)
            ? MapErrorType(statusCode)
            : errorType;

    private static string MapErrorType(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.NotFound => ErrorTypes.NotFound,
        (int)HttpStatusCode.TooManyRequests => ErrorTypes.TooManyRequests,
        >= 400 and < 500 => ErrorTypes.InvalidRequestError,
        _ => ErrorTypes.ServerError,
    };

    private static async IAsyncEnumerable<string> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<string> NormalizeNativeStreamChunks(
        IAsyncEnumerable<string> chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            yield return EnsureNullableResponseFields(chunk);
        }
    }

    private static string EnsureNullableResponseFields(string chunk)
    {
        if (chunk.Contains("\"prompt_cache_key\""))
            return chunk;

        // Upstream may omit prompt_cache_key; the spec requires it (nullable).
        if (chunk.Contains("\"prompt_cache_retention\""))
        {
            return chunk.Replace(
                "\"prompt_cache_retention\"",
                "\"prompt_cache_key\":null,\"prompt_cache_retention\"");
        }

        return chunk;
    }
}
