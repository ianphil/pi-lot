using System.Text.Json;
using LlmSvc.Core.Models;

namespace LlmSvc.Core.Services;

public sealed class ChatCompletionsTranslator
{
    public ChatCompletionRequest ToChatCompletionRequest(CreateResponseRequest request)
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
            Stream = false,
            MaxCompletionTokens = request.MaxOutputTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = tools,
            ToolChoice = toolChoice,
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
                Type = "server_error",
                Code = "invalid_upstream_response",
            });
        }

        return ToResponse(completion, request);
    }

    public Response ToResponse(ChatCompletionResponse completion, CreateResponseRequest request)
    {
        var choice = completion.Choices?.FirstOrDefault();
        var output = new List<ResponseItem>();
        var status = MapFinishReason(choice?.FinishReason);

        if (choice?.Message?.ToolCalls is { Length: > 0 })
        {
            foreach (var toolCall in choice.Message.ToolCalls)
            {
                output.Add(new ResponseFunctionCallItem
                {
                    Id = toolCall.Id ?? NewId("fc"),
                    Status = status,
                    Name = toolCall.Function?.Name ?? "function",
                    CallId = toolCall.Id ?? NewId("call"),
                    Arguments = toolCall.Function?.Arguments ?? "{}",
                });
            }
        }

        var text = ExtractChatText(choice?.Message?.Content);
        if (text is not null || output.Count == 0)
        {
            output.Add(new ResponseMessageItem
            {
                Id = NewId("msg"),
                Status = status,
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
            Usage = new ResponseUsage
            {
                InputTokens = completion.Usage?.PromptTokens ?? 0,
                OutputTokens = completion.Usage?.CompletionTokens ?? 0,
                TotalTokens = completion.Usage?.TotalTokens ?? 0,
            },
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            Tools = request.Tools,
            ToolChoice = CloneOrNull(request.ToolChoice),
        };
    }

    public Response NormalizeNativeResponse(string body, CreateResponseRequest request)
    {
        var direct = TryDeserializeCanonical(body);
        if (direct is not null)
        {
            return direct;
        }

        var native = JsonSerializer.Deserialize<ResponsesApiResponse>(body, JsonDefaults.Web);
        if (native is null)
        {
            throw new ResponseApiException(502, new ResponseError
            {
                Message = "Upstream responses payload could not be parsed.",
                Type = "server_error",
                Code = "invalid_upstream_response",
            });
        }

        var output = new List<ResponseItem>();
        foreach (var item in native.Output ?? [])
        {
            switch (item.Type)
            {
                case "function_call":
                    output.Add(new ResponseFunctionCallItem
                    {
                        Id = item.Id ?? NewId("fc"),
                        Status = item.Status ?? ResponseStatuses.Completed,
                        Name = item.Name ?? "function",
                        CallId = item.CallId ?? NewId("call"),
                        Arguments = item.Arguments ?? "{}",
                    });
                    break;

                default:
                    output.Add(new ResponseMessageItem
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
                    });
                    break;
            }
        }

        return new Response
        {
            Id = native.Id ?? NewId("resp"),
            CreatedAt = native.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = native.Status ?? ResponseStatuses.Completed,
            Model = native.Model ?? request.Model,
            Output = output.ToArray(),
            Usage = new ResponseUsage
            {
                InputTokens = native.Usage?.InputTokens ?? 0,
                OutputTokens = native.Usage?.OutputTokens ?? 0,
                TotalTokens = (native.Usage?.InputTokens ?? 0) + (native.Usage?.OutputTokens ?? 0),
            },
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            Tools = request.Tools,
            ToolChoice = CloneOrNull(request.ToolChoice),
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
            return CloneElement(value);
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
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                tools = tools
                    .Where(tool => tool.Function?.Name is not null && allowed.Contains(tool.Function.Name))
                    .ToArray();

                return "auto";
            }
        }

        return CloneElement(value);
    }

    private static IEnumerable<ChatMessage> ParseInput(JsonElement input)
    {
        if (input.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new ResponseApiException(400, new ResponseError
            {
                Message = "input is required",
                Type = "invalid_request_error",
                Param = "input",
                Code = "missing_required_parameter",
            });
        }

        if (input.ValueKind == JsonValueKind.String)
        {
            return
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = input.GetString(),
                },
            ];
        }

        if (input.ValueKind == JsonValueKind.Object)
        {
            return [ParseMessage(input)];
        }

        if (input.ValueKind == JsonValueKind.Array)
        {
            return input.EnumerateArray().Select(ParseMessage).ToArray();
        }

        throw new ResponseApiException(400, new ResponseError
        {
            Message = "input must be a string, object, or array",
            Type = "invalid_request_error",
            Param = "input",
            Code = "invalid_input_format",
        });
    }

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

    private static string? ExtractContentPartText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Object)
        {
            if (TryGetStringProperty(content, "text", out var text))
            {
                return text;
            }

            if (TryGetStringProperty(content, "output", out var output))
            {
                return output;
            }

            if (content.TryGetProperty("output", out var outputElement))
            {
                return ExtractOutputText(outputElement);
            }
        }

        return null;
    }

    private static string ExtractOutputText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("output", out var output))
        {
            return ExtractOutputText(output);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                Environment.NewLine,
                element.EnumerateArray()
                    .Select(ExtractContentPartText)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return element.GetRawText();
    }

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

    private static string MapFinishReason(string? finishReason) =>
        string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
            ? ResponseStatuses.Incomplete
            : ResponseStatuses.Completed;

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

    private static JsonElement? CloneOrNull(JsonElement? element) =>
        element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : CloneElement(element.Value);

    private static JsonElement CloneElement(JsonElement element) =>
        JsonDocument.Parse(element.GetRawText()).RootElement.Clone();

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

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
