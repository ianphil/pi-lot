using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmSvc.Core.Models;

namespace LlmSvc.Core.Services;

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

    public async IAsyncEnumerable<string> TranslateStream(
        IAsyncEnumerable<string> chunks,
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var state = new ChatCompletionResponseStreamState(request);

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var envelope = ParseSseChunk(chunk);
            if (envelope is null)
            {
                continue;
            }

            if (string.Equals(envelope.Value.Data, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            if (!TryDeserializeStreamChunk(envelope.Value.Data, out var streamChunk, out var errorMessage) || streamChunk is null)
            {
                foreach (var failureEvent in state.Fail(errorMessage ?? "Upstream chat completion stream could not be parsed."))
                {
                    yield return failureEvent;
                }

                yield break;
            }

            foreach (var startEvent in state.Start(streamChunk.Id, streamChunk.Model))
            {
                yield return startEvent;
            }

            state.ApplyUsage(streamChunk.Usage);

            foreach (var choice in streamChunk.Choices ?? [])
            {
                if (!string.IsNullOrWhiteSpace(choice.Delta?.Role))
                {
                    foreach (var output in state.ApplyRole(choice.Delta.Role!))
                    {
                        yield return output;
                    }
                }

                if (!string.IsNullOrEmpty(choice.Delta?.Content))
                {
                    foreach (var output in state.ApplyContentDelta(choice.Delta.Content!))
                    {
                        yield return output;
                    }
                }

                if (choice.Delta?.ToolCalls is { Length: > 0 })
                {
                    foreach (var output in state.ApplyToolCallDeltas(choice.Delta.ToolCalls))
                    {
                        yield return output;
                    }
                }

                if (!string.IsNullOrWhiteSpace(choice.FinishReason))
                {
                    state.Status = MapFinishReason(choice.FinishReason);
                }
            }
        }

        foreach (var completionEvent in state.Complete())
        {
            yield return completionEvent;
        }
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

    private static bool TryDeserializeStreamChunk(string data, out ChatCompletionChunk? chunk, out string? errorMessage)
    {
        try
        {
            chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonDefaults.Web);
            if (chunk is null)
            {
                errorMessage = "Upstream chat completion stream could not be parsed: Chunk payload was null.";
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            chunk = null;
            errorMessage = $"Upstream chat completion stream could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static (string? EventName, string Data)? ParseSseChunk(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return null;
        }

        using var reader = new StringReader(chunk);
        string? eventName = null;
        var data = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line[5..].TrimStart());
            }
        }

        if (eventName is null && data.Length == 0)
        {
            return null;
        }

        return (eventName, data.ToString());
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

    private sealed class ChatCompletionResponseStreamState
    {
        private readonly int? _maxOutputTokens;
        private readonly string? _requestModel;
        private readonly double? _temperature;
        private readonly ResponseFunctionToolDefinition[]? _tools;
        private readonly JsonElement? _toolChoice;
        private readonly double? _topP;
        private readonly Dictionary<int, ToolCallStreamState> _toolCalls = [];
        private int _nextOutputIndex;
        private int _sequence;
        private MessageStreamState? _message;

        public ChatCompletionResponseStreamState(CreateResponseRequest request)
        {
            _requestModel = request.Model;
            _temperature = request.Temperature;
            _topP = request.TopP;
            _maxOutputTokens = request.MaxOutputTokens;
            _tools = request.Tools;
            _toolChoice = CloneOrNull(request.ToolChoice);
        }

        public bool Started { get; private set; }

        public string ResponseId { get; private set; } = NewId("resp");

        public string? Model { get; private set; }

        public string Status { get; set; } = ResponseStatuses.Completed;

        public ResponseUsage? Usage { get; private set; }

        public IEnumerable<string> Start(string? responseId, string? model)
        {
            if (Started)
            {
                return [];
            }

            Started = true;
            ResponseId = responseId ?? ResponseId;
            Model = model ?? _requestModel;

            return
            [
                ResponseSseSerializer.SerializeEvent("response.created", new
                {
                    type = "response.created",
                    response = CreateResponse(ResponseStatuses.InProgress, [], null),
                }),
                ResponseSseSerializer.SerializeEvent("response.in_progress", new
                {
                    type = "response.in_progress",
                    response = CreateResponse(ResponseStatuses.InProgress, [], null),
                }),
            ];
        }

        public void ApplyUsage(UsageInfo? usage)
        {
            if (usage is null)
            {
                return;
            }

            Usage = new ResponseUsage
            {
                InputTokens = usage.PromptTokens,
                OutputTokens = usage.CompletionTokens,
                TotalTokens = usage.TotalTokens,
            };
        }

        public IEnumerable<string> ApplyRole(string role)
        {
            EnsureStarted();
            if (_message is not null)
            {
                return [];
            }

            _message = new MessageStreamState
            {
                Id = NewId("msg"),
                OutputIndex = _nextOutputIndex++,
                Role = role,
            };

            var item = CreateMessageItem(_message, ResponseStatuses.InProgress);
            return
            [
                ResponseSseSerializer.SerializeEvent("response.output_item.added", new
                {
                    type = "response.output_item.added",
                    sequence_number = _sequence++,
                    output_index = _message.OutputIndex,
                    item,
                }),
            ];
        }

        public IEnumerable<string> ApplyContentDelta(string delta)
        {
            EnsureStarted();
            var events = new List<string>();
            if (_message is null)
            {
                events.AddRange(ApplyRole("assistant"));
            }

            if (_message is null)
            {
                return events;
            }

            if (!_message.ContentStarted)
            {
                _message.ContentStarted = true;
                events.Add(ResponseSseSerializer.SerializeEvent("response.content_part.added", new
                {
                    type = "response.content_part.added",
                    sequence_number = _sequence++,
                    item_id = _message.Id,
                    output_index = _message.OutputIndex,
                    content_index = 0,
                    part = new
                    {
                        type = "output_text",
                        annotations = Array.Empty<object>(),
                        text = string.Empty,
                    },
                }));
            }

            _message.Text.Append(delta);
            events.Add(ResponseSseSerializer.SerializeEvent("response.output_text.delta", new
            {
                type = "response.output_text.delta",
                sequence_number = _sequence++,
                item_id = _message.Id,
                output_index = _message.OutputIndex,
                content_index = 0,
                delta,
            }));

            return events;
        }

        public IEnumerable<string> ApplyToolCallDeltas(IEnumerable<ChatChunkToolCall> deltas)
        {
            EnsureStarted();
            var events = new List<string>();

            foreach (var delta in deltas)
            {
                var index = delta.Index ?? 0;
                if (!_toolCalls.TryGetValue(index, out var state))
                {
                    state = new ToolCallStreamState
                    {
                        Id = NewId("fc"),
                        CallId = delta.Id ?? NewId("call"),
                        OutputIndex = _nextOutputIndex++,
                    };
                    _toolCalls[index] = state;
                }

                if (!string.IsNullOrWhiteSpace(delta.Id))
                {
                    state.CallId = delta.Id!;
                }

                if (!string.IsNullOrWhiteSpace(delta.Function?.Name))
                {
                    state.Name = delta.Function.Name!;
                }

                if (!state.Added)
                {
                    state.Added = true;
                    var item = CreateFunctionCallItem(state, ResponseStatuses.InProgress);
                    events.Add(ResponseSseSerializer.SerializeEvent("response.output_item.added", new
                    {
                        type = "response.output_item.added",
                        sequence_number = _sequence++,
                        output_index = state.OutputIndex,
                        item,
                    }));
                }

                if (!string.IsNullOrEmpty(delta.Function?.Arguments))
                {
                    state.Arguments.Append(delta.Function.Arguments);
                    events.Add(ResponseSseSerializer.SerializeEvent("response.function_call_arguments.delta", new
                    {
                        type = "response.function_call_arguments.delta",
                        sequence_number = _sequence++,
                        item_id = state.Id,
                        output_index = state.OutputIndex,
                        delta = delta.Function.Arguments,
                    }));
                }
            }

            return events;
        }

        public IEnumerable<string> Complete()
        {
            EnsureStarted();

            var events = new List<string>();
            var finalStatus = string.IsNullOrWhiteSpace(Status) ? ResponseStatuses.Completed : Status;

            if (_message is not null)
            {
                if (_message.ContentStarted)
                {
                    var text = _message.Text.ToString();
                    events.Add(ResponseSseSerializer.SerializeEvent("response.output_text.done", new
                    {
                        type = "response.output_text.done",
                        sequence_number = _sequence++,
                        item_id = _message.Id,
                        output_index = _message.OutputIndex,
                        content_index = 0,
                        text,
                    }));

                    events.Add(ResponseSseSerializer.SerializeEvent("response.content_part.done", new
                    {
                        type = "response.content_part.done",
                        sequence_number = _sequence++,
                        item_id = _message.Id,
                        output_index = _message.OutputIndex,
                        content_index = 0,
                        part = new
                        {
                            type = "output_text",
                            annotations = Array.Empty<object>(),
                            text,
                        },
                    }));
                }

                events.Add(ResponseSseSerializer.SerializeEvent("response.output_item.done", new
                {
                    type = "response.output_item.done",
                    sequence_number = _sequence++,
                    output_index = _message.OutputIndex,
                    item = CreateMessageItem(_message, finalStatus),
                }));
            }

            foreach (var toolState in _toolCalls.Values.OrderBy(state => state.OutputIndex))
            {
                events.Add(ResponseSseSerializer.SerializeEvent("response.function_call_arguments.done", new
                {
                    type = "response.function_call_arguments.done",
                    sequence_number = _sequence++,
                    item_id = toolState.Id,
                    output_index = toolState.OutputIndex,
                    arguments = toolState.Arguments.ToString(),
                }));

                events.Add(ResponseSseSerializer.SerializeEvent("response.output_item.done", new
                {
                    type = "response.output_item.done",
                    sequence_number = _sequence++,
                    output_index = toolState.OutputIndex,
                    item = CreateFunctionCallItem(toolState, finalStatus),
                }));
            }

            var response = CreateResponse(finalStatus, BuildOutputItems(finalStatus), null);
            events.Add(ResponseSseSerializer.SerializeEvent("response.completed", new
            {
                type = "response.completed",
                response,
            }));
            events.Add(ResponseSseSerializer.SerializeDone());

            return events;
        }

        public IEnumerable<string> Fail(string message)
        {
            EnsureStarted();

            var error = new ResponseError
            {
                Message = message,
                Type = "server_error",
                Code = "stream_error",
            };

            return
            [
                ResponseSseSerializer.SerializeEvent("error", new
                {
                    type = "error",
                    error,
                }),
                ResponseSseSerializer.SerializeEvent("response.failed", new
                {
                    type = "response.failed",
                    response = CreateResponse(ResponseStatuses.Failed, BuildOutputItems(ResponseStatuses.Failed), error),
                }),
                ResponseSseSerializer.SerializeDone(),
            ];
        }

        private void EnsureStarted()
        {
            if (Started)
            {
                return;
            }

            Started = true;
            Model = _requestModel;
        }

        private ResponseItem[] BuildOutputItems(string status)
        {
            var items = new List<(int OutputIndex, ResponseItem Item)>();

            if (_message is not null)
            {
                items.Add((_message.OutputIndex, CreateMessageItem(_message, status)));
            }

            foreach (var toolState in _toolCalls.Values)
            {
                items.Add((toolState.OutputIndex, CreateFunctionCallItem(toolState, status)));
            }

            return items
                .OrderBy(item => item.OutputIndex)
                .Select(item => item.Item)
                .ToArray();
        }

        private Response CreateResponse(string status, ResponseItem[] output, ResponseError? error) => new()
        {
            Id = ResponseId,
            Status = status,
            Model = Model ?? _requestModel,
            Output = output,
            Usage = Usage,
            Error = error,
            Temperature = _temperature,
            TopP = _topP,
            MaxOutputTokens = (int?)_maxOutputTokens,
            Tools = _tools,
            ToolChoice = _toolChoice,
        };

        private static ResponseMessageItem CreateMessageItem(MessageStreamState state, string status)
        {
            var content = state.ContentStarted
                ? new ResponseContentPart[]
                {
                    new ResponseOutputTextPart
                    {
                        Text = state.Text.ToString(),
                    },
                }
                : [];

            return new ResponseMessageItem
            {
                Id = state.Id,
                Status = status,
                Role = state.Role,
                Content = content,
            };
        }

        private static ResponseFunctionCallItem CreateFunctionCallItem(ToolCallStreamState state, string status) => new()
        {
            Id = state.Id,
            Status = status,
            Name = state.Name ?? "function",
            CallId = state.CallId,
            Arguments = state.Arguments.ToString(),
        };
    }

    private sealed class MessageStreamState
    {
        public required string Id { get; init; }

        public required int OutputIndex { get; init; }

        public string Role { get; set; } = "assistant";

        public bool ContentStarted { get; set; }

        public StringBuilder Text { get; } = new();
    }

    private sealed class ToolCallStreamState
    {
        public required string Id { get; init; }

        public required string CallId { get; set; }

        public required int OutputIndex { get; init; }

        public string? Name { get; set; }

        public bool Added { get; set; }

        public StringBuilder Arguments { get; } = new();
    }
}
