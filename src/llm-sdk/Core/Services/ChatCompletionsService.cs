using System.Net;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSdk.Core.Services;

public sealed class ChatCompletionsService : IChatCompletionsService
{
    private readonly IModelProvider _provider;
    private readonly ResponsesStreamToChatTranslator _streamTranslator;

    public ChatCompletionsService(IModelProvider provider)
        : this(provider, new ResponsesStreamToChatTranslator())
    {
    }

    public ChatCompletionsService(IModelProvider provider, ResponsesStreamToChatTranslator streamTranslator)
    {
        _provider = provider;
        _streamTranslator = streamTranslator;
    }

    public async Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Validate(request);

            var models = await _provider.FetchModelsAsync(cancellationToken: cancellationToken);
            var model = models.FirstOrDefault(m =>
                string.Equals(m.Id, request.Model, StringComparison.OrdinalIgnoreCase));

            if (model is null)
            {
                return MakeErrorResult(404, $"Model '{request.Model}' not found", "model_not_found");
            }

            var useResponses = !model.SupportsChatCompletions && model.SupportsResponses;

            if (request.Stream == true)
            {
                return await HandleStreamingAsync(request, model, useResponses, cancellationToken);
            }

            return await HandleNonStreamingAsync(request, useResponses, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return MakeErrorResult(502, "Failed to process upstream response.", "upstream_error");
        }
    }

    private async Task<ResponseHttpResult> HandleStreamingAsync(
        ChatCompletionRequest request, ModelInfo model, bool useResponses, CancellationToken cancellationToken)
    {
        if (useResponses)
        {
            var responsesRequest = MapToResponsesRequest(request, stream: true);
            var upstream = await _provider.StreamResponsesAsync(responsesRequest, cancellationToken);
            if (upstream.StatusCode >= 400)
            {
                return NormalizeStreamError(upstream);
            }

            return ResponseHttpResult.FromStream(
                _streamTranslator.TranslateStream(upstream.Chunks ?? EmptyChunks(), request, cancellationToken),
                200,
                "text/event-stream",
                upstream.Headers);
        }

        if (model.SupportsChatCompletions)
        {
            var upstream = await _provider.StreamChatCompletionsAsync(
                CloneForStreaming(request), cancellationToken);
            if (upstream.StatusCode >= 400)
            {
                return NormalizeStreamError(upstream);
            }

            return ResponseHttpResult.FromStream(
                upstream.Chunks ?? EmptyChunks(),
                upstream.StatusCode,
                upstream.ContentType,
                upstream.Headers);
        }

        return MakeErrorResult(400,
            $"Model '{request.Model}' does not support /chat/completions or /responses.",
            "unsupported_model_endpoint");
    }

    private async Task<ResponseHttpResult> HandleNonStreamingAsync(
        ChatCompletionRequest request, bool useResponses, CancellationToken cancellationToken)
    {
        ProxyHttpResult upstream;

        if (useResponses)
        {
            var responsesRequest = MapToResponsesRequest(request, stream: false);
            upstream = await _provider.SendResponsesAsync(responsesRequest, cancellationToken);
        }
        else
        {
            upstream = await _provider.SendChatCompletionsAsync(
                CloneWithoutStreaming(request), cancellationToken);
        }

        var body = upstream.Body;
        if (useResponses && upstream.StatusCode is >= 200 and < 300)
        {
            body = ChatCompletionBodyTranslator.TranslateResponseBodyToChatCompletion(body);
        }

        return ResponseHttpResult.FromBody(body, upstream.StatusCode, "application/json", upstream.Headers);
    }

    private static void Validate(ChatCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ChatCompletionsException(400, "model is required", "invalid_request");
        }
    }

    internal static CreateResponseRequest MapToResponsesRequest(ChatCompletionRequest request, bool stream)
    {
        return new CreateResponseRequest
        {
            Model = request.Model,
            Input = JsonDocument.Parse(
                JsonSerializer.Serialize(MapChatMessagesToResponsesInput(request.Messages), JsonDefaults.Web)).RootElement.Clone(),
            Stream = stream,
            MaxOutputTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools?
                .Where(t => t.Function is not null)
                .Select(t => new ResponseFunctionToolDefinition
                {
                    Name = t.Function!.Name!,
                    Description = t.Function.Description,
                    Parameters = t.Function.Parameters,
                }).ToArray(),
            ToolChoice = request.ToolChoice is not null
                ? JsonDocument.Parse(JsonSerializer.Serialize(request.ToolChoice, JsonDefaults.Web)).RootElement.Clone()
                : null,
            Headers = request.Headers,
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            TimeoutMs = request.TimeoutMs,
            MaxRetries = request.MaxRetries,
            MaxRetryDelayMs = request.MaxRetryDelayMs,
        };
    }

    private static object[] MapChatMessagesToResponsesInput(ChatMessage[]? messages)
    {
        if (messages is not { Length: > 0 })
        {
            return [];
        }

        var items = new List<object>();
        foreach (var message in messages)
        {
            if (message.ToolCalls is { Length: > 0 })
            {
                foreach (var toolCall in message.ToolCalls)
                {
                    items.Add(new
                    {
                        type = "function_call",
                        call_id = toolCall.Id,
                        name = toolCall.Function?.Name,
                        arguments = toolCall.Function?.Arguments ?? "{}",
                    });
                }
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new
                {
                    type = "function_call_output",
                    call_id = message.ToolCallId,
                    output = ExtractToolOutput(message.Content),
                });
                continue;
            }

            var contentType = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "output_text"
                : "input_text";
            var content = NormalizeResponsesContent(message.Content, contentType);
            var hasContent = content is { Length: > 0 };
            if (!hasContent && message.ToolCalls is { Length: > 0 })
            {
                continue;
            }

            items.Add(new
            {
                type = "message",
                role = message.Role,
                content = hasContent
                    ? content
                    : [new { type = contentType, text = string.Empty }],
            });
        }

        return items.ToArray();
    }

    private static object[]? NormalizeResponsesContent(object? content, string contentType)
    {
        var normalized = ChatCompletionBodyTranslator.NormalizeMessageContent(content);
        return normalized switch
        {
            null => null,
            string text => [new { type = contentType, text }],
            object[] values => values.SelectMany(v => MapContentValue(v, contentType)).ToArray(),
            _ => [new { type = contentType, text = JsonSerializer.Serialize(normalized, JsonDefaults.Web) }],
        };
    }

    private static IEnumerable<object> MapContentValue(object? value, string contentType)
    {
        if (value is null)
        {
            yield break;
        }

        if (value is string text)
        {
            yield return new { type = contentType, text };
            yield break;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                yield return JsonSerializer.Deserialize<object>(element.GetRawText(), JsonDefaults.Web)
                    ?? new { type = contentType, text = element.GetRawText() };
                yield break;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                yield return new { type = contentType, text = element.GetString() ?? string.Empty };
                yield break;
            }
        }

        yield return new { type = contentType, text = JsonSerializer.Serialize(value, JsonDefaults.Web) };
    }

    private static string ExtractToolOutput(object? content) => content switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(content, JsonDefaults.Web),
    };

    private static ChatCompletionRequest CloneWithoutStreaming(ChatCompletionRequest request) => new()
    {
        Model = request.Model,
        Messages = request.Messages,
        Stream = false,
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

    private static ChatCompletionRequest CloneForStreaming(ChatCompletionRequest request) => new()
    {
        Model = request.Model,
        Messages = request.Messages,
        Stream = true,
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

    private static ResponseHttpResult MakeErrorResult(int statusCode, string message, string code)
    {
        var error = new OpenAIErrorResponse
        {
            Error = new OpenAIError { Message = message, Code = code, Type = "error" },
        };
        return ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(error, JsonDefaults.Web),
            statusCode,
            "application/json");
    }

    private static ResponseHttpResult NormalizeStreamError(ProxyStreamResult upstream)
    {
        return ResponseHttpResult.FromBody(
            upstream.Body ?? "{\"error\":{\"message\":\"Upstream streaming request failed.\"}}",
            upstream.StatusCode,
            "application/json",
            upstream.Headers);
    }

    private static async IAsyncEnumerable<string> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }
}

public sealed class ChatCompletionsException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ChatCompletionsException(int statusCode, string message, string code) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
