using System.Text.Json;
using LlmSdk.Core.Models;
using static LlmSdk.Core.Models.JsonElementHelpers;

namespace LlmSdk.Core.Services;

public sealed class ChatCompletionsTranslator
{
    public ChatCompletionRequest ToChatCompletionRequest(CreateResponseRequest request, bool stream = false)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = request.Instructions,
            });
        }

        messages.AddRange(ParseInput(request.Input));

        var tools = request.Tools?.Select(MapTool).ToArray();
        var toolChoice = MapToolChoice(request.ToolChoice, ref tools);

        return new ChatCompletionRequest
        {
            Model = request.Model,
            Messages = messages.ToArray(),
            Stream = stream,
            MaxCompletionTokens = request.MaxOutputTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = tools,
            ToolChoice = toolChoice,
            Headers = request.Headers,
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            TimeoutMs = request.TimeoutMs,
            MaxRetries = request.MaxRetries,
            MaxRetryDelayMs = request.MaxRetryDelayMs,
            Metadata = request.Metadata as IReadOnlyDictionary<string, string>,
            OnPayload = request.OnPayload,
            OnResponse = request.OnResponse,
        };
    }

    public Response ToResponse(string chatCompletionBody, CreateResponseRequest request)
    {
        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(chatCompletionBody, JsonDefaults.Web);
        if (completion is null)
        {
            throw new ResponseApiException(502, new ResponseError
            {
                Message = "Upstream chat completion response could not be parsed.",
                Type = ErrorTypes.ServerError,
                Code = ErrorCodes.InvalidUpstreamResponse,
            });
        }

        return ToResponse(completion, request);
    }

    public Response ToResponse(ChatCompletionResponse completion, CreateResponseRequest request)
    {
        var choice = completion.Choices?.FirstOrDefault();
        var output = new List<ResponseItem>();
        var status = MapFinishReason(choice?.FinishReason);
        var incompleteDetails = MapIncompleteDetails(choice?.FinishReason);
        var toolCalls = choice?.Message?.ToolCalls ?? [];
        var text = ExtractChatText(choice?.Message?.Content);
        var shouldAddMessage = text is not null || toolCalls.Length == 0;

        if (toolCalls.Length > 0)
        {
            for (var toolCallIndex = 0; toolCallIndex < toolCalls.Length; toolCallIndex++)
            {
                var toolCall = toolCalls[toolCallIndex];
                output.Add(new ResponseFunctionCallItem
                {
                    Id = toolCall.Id ?? NewId("fc"),
                    Status = MapOutputItemStatus(status, isLastItem: !shouldAddMessage && toolCallIndex == toolCalls.Length - 1),
                    Name = toolCall.Function?.Name ?? "function",
                    CallId = toolCall.Id ?? NewId("call"),
                    Arguments = toolCall.Function?.Arguments ?? "{}",
                });
            }
        }

        if (shouldAddMessage)
        {
            output.Add(new ResponseMessageItem
            {
                Id = NewId("msg"),
                Status = MapOutputItemStatus(status, isLastItem: true),
                Role = choice?.Message?.Role ?? "assistant",
                Content =
                [
                    new ResponseOutputTextPart
                    {
                        Text = text ?? string.Empty,
                    },
                ],
            });
        }

        return new Response
        {
            Id = completion.Id ?? NewId("resp"),
            Status = status,
            Model = completion.Model ?? request.Model,
            Output = output.ToArray(),
            CompletedAt = status == ResponseStatuses.Completed ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : null,
            Usage = new ResponseUsage
            {
                InputTokens = completion.Usage?.PromptTokens ?? 0,
                OutputTokens = completion.Usage?.CompletionTokens ?? 0,
                TotalTokens = completion.Usage?.TotalTokens ?? 0,
            },
            IncompleteDetails = incompleteDetails,
            Error = null,
            Temperature = request.Temperature ?? 1.0,
            TopP = request.TopP ?? 1.0,
            MaxOutputTokens = request.MaxOutputTokens,
            Tools = request.Tools ?? [],
            ToolChoice = (object?)CloneOrNull(request.ToolChoice) ?? "auto",
            PreviousResponseId = request.PreviousResponseId,
            Instructions = request.Instructions,
            Truncation = request.Truncation ?? "disabled",
            ParallelToolCalls = request.ParallelToolCalls ?? true,
            Text = request.Text ?? new ResponseTextConfig(),
            PresencePenalty = request.PresencePenalty ?? 0.0,
            FrequencyPenalty = request.FrequencyPenalty ?? 0.0,
            TopLogprobs = request.TopLogprobs ?? 0,
            Store = request.Store ?? false,
            Background = request.Background ?? false,
            ServiceTier = request.ServiceTier ?? "default",
            Metadata = request.Metadata,
            MaxToolCalls = request.MaxToolCalls,
            Reasoning = request.Reasoning,
        };
    }

    public Response NormalizeNativeResponse(string body, CreateResponseRequest request)
    {
        var direct = TryDeserializeCanonical(body);
        if (direct is not null)
        {
            return FilterEmptyMessageItems(direct);
        }

        var native = JsonSerializer.Deserialize<ResponsesApiResponse>(body, JsonDefaults.Web);
        if (native is null)
        {
            throw new ResponseApiException(502, new ResponseError
            {
                Message = "Upstream responses payload could not be parsed.",
                Type = ErrorTypes.ServerError,
                Code = ErrorCodes.InvalidUpstreamResponse,
            });
        }

        var output = new List<ResponseItem>();
        foreach (var item in native.Output ?? [])
        {
            ResponseItem mapped = item.Type switch
            {
                "function_call" => new ResponseFunctionCallItem
                {
                    Id = item.Id ?? NewId("fc"),
                    Status = item.Status ?? ResponseStatuses.Completed,
                    Name = item.Name ?? "function",
                    CallId = item.CallId ?? NewId("call"),
                    Arguments = item.Arguments ?? "{}",
                },
                _ => new ResponseMessageItem
                {
                    Id = item.Id ?? NewId("msg"),
                    Status = item.Status ?? ResponseStatuses.Completed,
                    Role = item.Role ?? "assistant",
                    Content = item.Content?
                        .Select(content => (ResponseContentPart)new ResponseOutputTextPart
                        {
                            Text = content.Text ?? string.Empty,
                            Annotations = content.Annotations ?? [],
                        })
                        .ToArray() ?? [],
                },
            };
            output.Add(mapped);
        }

        return new Response
        {
            Id = native.Id ?? NewId("resp"),
            CreatedAt = native.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = native.Status ?? ResponseStatuses.Completed,
            Model = native.Model ?? request.Model,
            Output = output.ToArray(),
            CompletedAt = (native.Status ?? ResponseStatuses.Completed) == ResponseStatuses.Completed
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : null,
            Usage = new ResponseUsage
            {
                InputTokens = native.Usage?.InputTokens ?? 0,
                OutputTokens = native.Usage?.OutputTokens ?? 0,
                TotalTokens = (native.Usage?.InputTokens ?? 0) + (native.Usage?.OutputTokens ?? 0),
            },
            IncompleteDetails = native.IncompleteDetails,
            Error = null,
            Temperature = request.Temperature ?? 1.0,
            TopP = request.TopP ?? 1.0,
            MaxOutputTokens = request.MaxOutputTokens,
            Tools = request.Tools ?? [],
            ToolChoice = (object?)CloneOrNull(request.ToolChoice) ?? "auto",
            PreviousResponseId = request.PreviousResponseId,
            Instructions = request.Instructions,
            Truncation = request.Truncation ?? "disabled",
            ParallelToolCalls = request.ParallelToolCalls ?? true,
            Text = request.Text ?? new ResponseTextConfig(),
            PresencePenalty = request.PresencePenalty ?? 0.0,
            FrequencyPenalty = request.FrequencyPenalty ?? 0.0,
            TopLogprobs = request.TopLogprobs ?? 0,
            Store = request.Store ?? false,
            Background = request.Background ?? false,
            ServiceTier = request.ServiceTier ?? "default",
            Metadata = request.Metadata,
            MaxToolCalls = request.MaxToolCalls,
            Reasoning = request.Reasoning,
        };
    }

    private static ChatToolDefinition MapTool(ResponseFunctionToolDefinition tool) => new()
    {
        Type = tool.Type,
        Function = new ChatToolFunctionDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = CloneOrNull(tool.Parameters),
            Strict = tool.Strict,
        },
    };

    private static object? MapToolChoice(JsonElement? toolChoice, ref ChatToolDefinition[]? tools)
    {
        if (toolChoice is null || toolChoice.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var value = toolChoice.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return Clone(value);
        }

        if (TryGetStringProperty(value, "type", out var type))
        {
            if (string.Equals(type, "function", StringComparison.OrdinalIgnoreCase) &&
                TryGetStringProperty(value, "name", out var name))
            {
                return new
                {
                    type = "function",
                    function = new
                    {
                        name,
                    },
                };
            }

            if (string.Equals(type, "allowed_tools", StringComparison.OrdinalIgnoreCase) &&
                value.TryGetProperty("tools", out var allowedToolsElement) &&
                allowedToolsElement.ValueKind == JsonValueKind.Array &&
                tools is not null)
            {
                var allowed = allowedToolsElement.EnumerateArray()
                    .Select(item => TryGetStringProperty(item, "name", out var toolName) ? toolName : null)
                    .Where(name => name is not null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                tools = tools
                    .Where(tool => tool.Function?.Name is not null && allowed.Contains(tool.Function.Name))
                    .ToArray();

                return "auto";
            }
        }

        return Clone(value);
    }

    private static IEnumerable<ChatMessage> ParseInput(JsonElement input) => input.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => throw new ResponseApiException(400, new ResponseError
        {
            Message = "input is required",
            Type = ErrorTypes.InvalidRequestError,
            Param = "input",
            Code = ErrorCodes.MissingRequiredParameter,
        }),
        JsonValueKind.String =>
        [
            new ChatMessage
            {
                Role = "user",
                Content = input.GetString(),
            },
        ],
        JsonValueKind.Object => [ParseMessage(input)],
        JsonValueKind.Array => input.EnumerateArray().Select(ParseMessage).ToArray(),
        _ => throw new ResponseApiException(400, new ResponseError
        {
            Message = "input must be a string, object, or array",
            Type = ErrorTypes.InvalidRequestError,
            Param = "input",
            Code = ErrorCodes.InvalidInputFormat,
        }),
    };

    private static ChatMessage ParseMessage(JsonElement element)
    {
        if (TryGetStringProperty(element, "type", out var itemType))
        {
            if (string.Equals(itemType, "function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = GetOptionalStringProperty(element, "call_id"),
                    Content = ExtractOutputText(element),
                };
            }

            if (string.Equals(itemType, "function_call", StringComparison.OrdinalIgnoreCase))
            {
                var id = GetOptionalStringProperty(element, "call_id") ?? NewId("call");
                return new ChatMessage
                {
                    Role = "assistant",
                    Content = null,
                    ToolCalls =
                    [
                        new ChatToolCall
                        {
                            Id = id,
                            Function = new ChatToolCallFunction
                            {
                                Name = GetOptionalStringProperty(element, "name"),
                                Arguments = GetOptionalStringProperty(element, "arguments") ?? "{}",
                            },
                        },
                    ],
                };
            }
        }

        var role = GetOptionalStringProperty(element, "role") ?? "user";
        object? content = null;
        if (element.TryGetProperty("content", out var contentElement))
        {
            content = ExtractContent(contentElement);
        }

        return new ChatMessage
        {
            Role = role,
            Content = content,
        };
    }

    private static object? ExtractContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Select(ExtractContentPartText)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Object => ExtractContentPartText(content),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => content.GetRawText(),
        };
    }

    private static string? ExtractContentPartText(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => content.GetString(),
        JsonValueKind.Object when TryGetStringProperty(content, "text", out var text) => text,
        JsonValueKind.Object when TryGetStringProperty(content, "output", out var output) => output,
        JsonValueKind.Object when content.TryGetProperty("output", out var outputElement) => ExtractOutputText(outputElement),
        _ => null,
    };

    private static string ExtractOutputText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Object when element.TryGetProperty("output", out var output) => ExtractOutputText(output),
        JsonValueKind.Array => string.Join(
            Environment.NewLine,
            element.EnumerateArray()
                .Select(ExtractContentPartText)
                .Where(text => !string.IsNullOrWhiteSpace(text))),
        _ => element.GetRawText(),
    };

    private static string? ExtractChatText(object? content)
    {
        return content switch
        {
            null => null,
            string text => text,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Array => string.Join(
                    Environment.NewLine,
                    element.EnumerateArray()
                        .Select(ExtractContentPartText)
                        .Where(text => !string.IsNullOrWhiteSpace(text))),
                JsonValueKind.Object => ExtractContentPartText(element),
                _ => element.GetRawText(),
            },
            _ => content.ToString(),
        };
    }

    internal static string MapFinishReason(string? finishReason) =>
        string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
            ? ResponseStatuses.Incomplete
            : ResponseStatuses.Completed;

    internal static ResponseIncompleteDetails? MapIncompleteDetails(string? finishReason) =>
        MapIncompleteDetailsForStatus(MapFinishReason(finishReason));

    internal static ResponseIncompleteDetails? MapIncompleteDetailsForStatus(string? status) =>
        string.Equals(status, ResponseStatuses.Incomplete, StringComparison.OrdinalIgnoreCase)
            ? new ResponseIncompleteDetails
            {
                Reason = "max_output_tokens",
            }
            : null;

    internal static string MapOutputItemStatus(string responseStatus, bool isLastItem) =>
        string.Equals(responseStatus, ResponseStatuses.Incomplete, StringComparison.OrdinalIgnoreCase) && !isLastItem
            ? ResponseStatuses.Completed
            : responseStatus;

    private static Response? TryDeserializeCanonical(string body)
    {
        try
        {
            var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
            return string.Equals(response?.Object, "response", StringComparison.OrdinalIgnoreCase)
                ? response
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static Response FilterEmptyMessageItems(Response response)
    {
        var filtered = response.Output
            .Where(item => item is not ResponseMessageItem msg || msg.Content.Length > 0)
            .ToArray();

        if (filtered.Length == response.Output.Length)
            return response;

        return new Response
        {
            Id = response.Id,
            Object = response.Object,
            CreatedAt = response.CreatedAt,
            Status = response.Status,
            Model = response.Model,
            Output = filtered,
            CompletedAt = response.CompletedAt,
            Usage = response.Usage,
            Error = response.Error,
            IncompleteDetails = response.IncompleteDetails,
            Temperature = response.Temperature,
            TopP = response.TopP,
            MaxOutputTokens = response.MaxOutputTokens,
            Tools = response.Tools,
            ToolChoice = response.ToolChoice,
            PreviousResponseId = response.PreviousResponseId,
            Instructions = response.Instructions,
            Truncation = response.Truncation,
            ParallelToolCalls = response.ParallelToolCalls,
            Text = response.Text,
            PresencePenalty = response.PresencePenalty,
            FrequencyPenalty = response.FrequencyPenalty,
            TopLogprobs = response.TopLogprobs,
            Store = response.Store,
            Background = response.Background,
            ServiceTier = response.ServiceTier,
            Metadata = response.Metadata,
            MaxToolCalls = response.MaxToolCalls,
            Reasoning = response.Reasoning,
            SafetyIdentifier = response.SafetyIdentifier,
            PromptCacheKey = response.PromptCacheKey,
        };
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    private static string? GetOptionalStringProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public static string TranslateResponseBodyToChatCompletion(string responsesBody) =>
        ChatCompletionBodyTranslator.TranslateResponseBodyToChatCompletion(responsesBody);

    internal static object? NormalizeMessageContent(object? content) =>
        ChatCompletionBodyTranslator.NormalizeMessageContent(content);
}
