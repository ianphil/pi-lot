using System.Net;
using System.Text.Json;
using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;

namespace LlmSvc.Core.Services;

public sealed class ResponsesService : IResponsesService
{
    private readonly IModelProvider _provider;
    private readonly ChatCompletionsTranslator _translator;

    public ResponsesService(IModelProvider provider, ChatCompletionsTranslator translator)
    {
        _provider = provider;
        _translator = translator;
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
                    Type = "invalid_request_error",
                    Param = "model",
                    Code = "model_not_found",
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
                        upstream.Chunks ?? EmptyChunks(),
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
                        _translator.TranslateStream(upstream.Chunks ?? EmptyChunks(), request, cancellationToken),
                        200,
                        "text/event-stream");
                }

                throw new ResponseApiException(400, new ResponseError
                {
                    Message = $"Model '{request.Model}' does not support /responses or /chat/completions.",
                    Type = "invalid_request_error",
                    Param = "model",
                    Code = "unsupported_model_endpoint",
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
                    Type = "invalid_request_error",
                    Param = "model",
                    Code = "unsupported_model_endpoint",
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
                Type = "invalid_request_error",
                Param = "model",
                Code = "missing_required_parameter",
            });
        }

        if (request.Input.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new ResponseApiException(400, new ResponseError
            {
                Message = "input is required",
                Type = "invalid_request_error",
                Param = "input",
                Code = "missing_required_parameter",
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
    };

    private static JsonElement CloneOrDefault(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? default
            : JsonDocument.Parse(element.GetRawText()).RootElement.Clone();

    private static JsonElement? CloneOrNull(JsonElement? element) =>
        element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : JsonDocument.Parse(element.Value.GetRawText()).RootElement.Clone();

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
        (int)HttpStatusCode.NotFound => "not_found",
        (int)HttpStatusCode.TooManyRequests => "too_many_requests",
        >= 400 and < 500 => "invalid_request_error",
        _ => "server_error",
    };

    private static async IAsyncEnumerable<string> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }
}
