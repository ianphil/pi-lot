using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmSdk.Core.Models;
using static LlmSdk.Core.Models.JsonElementHelpers;

namespace LlmSdk.Core.Services;

public sealed class ChatCompletionsStreamTranslator
{
    public async IAsyncEnumerable<string> TranslateStream(
        IAsyncEnumerable<string> chunks,
        CreateResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var state = new ChatCompletionResponseStreamState(request);

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var envelope = SseChunkParser.Parse(chunk);
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
                    state.Status = ChatCompletionsTranslator.MapFinishReason(choice.FinishReason);
                }
            }
        }

        foreach (var completionEvent in state.Complete())
        {
            yield return completionEvent;
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

    private sealed class ChatCompletionResponseStreamState
    {
        private readonly int? _maxOutputTokens;
        private readonly string? _requestModel;
        private readonly double? _temperature;
        private readonly ResponseFunctionToolDefinition[]? _tools;
        private readonly JsonElement? _toolChoice;
        private readonly double? _topP;
        private readonly string? _instructions;
        private readonly string? _previousResponseId;
        private readonly string _truncation;
        private readonly bool _parallelToolCalls;
        private readonly ResponseTextConfig _text;
        private readonly double _presencePenalty;
        private readonly double _frequencyPenalty;
        private readonly int _topLogprobs;
        private readonly bool _store;
        private readonly bool _background;
        private readonly string _serviceTier;
        private readonly object? _metadata;
        private readonly int? _maxToolCalls;
        private readonly ResponseReasoning? _reasoning;
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
            _instructions = request.Instructions;
            _previousResponseId = request.PreviousResponseId;
            _truncation = request.Truncation ?? "disabled";
            _parallelToolCalls = request.ParallelToolCalls ?? true;
            _text = request.Text ?? new ResponseTextConfig();
            _presencePenalty = request.PresencePenalty ?? 0.0;
            _frequencyPenalty = request.FrequencyPenalty ?? 0.0;
            _topLogprobs = request.TopLogprobs ?? 0;
            _store = request.Store ?? false;
            _background = request.Background ?? false;
            _serviceTier = request.ServiceTier ?? "default";
            _metadata = request.Metadata;
            _maxToolCalls = request.MaxToolCalls;
            _reasoning = request.Reasoning;
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
                    sequence_number = _sequence++,
                    response = CreateResponse(ResponseStatuses.InProgress, [], null),
                }),
                ResponseSseSerializer.SerializeEvent("response.in_progress", new
                {
                    type = "response.in_progress",
                    sequence_number = _sequence++,
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
                        logprobs = Array.Empty<object>(),
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
                logprobs = Array.Empty<object>(),
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
            var lastOutputIndex = GetLastOutputIndex();

            if (_message is not null)
            {
                var messageStatus = ChatCompletionsTranslator.MapOutputItemStatus(finalStatus, _message.OutputIndex == lastOutputIndex);
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
                        logprobs = Array.Empty<object>(),
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
                            logprobs = Array.Empty<object>(),
                        },
                    }));
                }

                events.Add(ResponseSseSerializer.SerializeEvent("response.output_item.done", new
                {
                    type = "response.output_item.done",
                    sequence_number = _sequence++,
                    output_index = _message.OutputIndex,
                    item = CreateMessageItem(_message, messageStatus),
                }));
            }

            foreach (var toolState in _toolCalls.Values.OrderBy(state => state.OutputIndex))
            {
                var toolStatus = ChatCompletionsTranslator.MapOutputItemStatus(finalStatus, toolState.OutputIndex == lastOutputIndex);
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
                    item = CreateFunctionCallItem(toolState, toolStatus),
                }));
            }

            var response = CreateResponse(finalStatus, BuildOutputItems(finalStatus, lastOutputIndex), null);
            var terminalEventName = ResponseSseSerializer.GetTerminalEventName(finalStatus);
            events.Add(ResponseSseSerializer.SerializeEvent(terminalEventName, new
            {
                type = terminalEventName,
                sequence_number = _sequence++,
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
                Type = ErrorTypes.ServerError,
                Code = ErrorCodes.StreamError,
            };

            return
            [
                ResponseSseSerializer.SerializeEvent("error", new
                {
                    type = "error",
                    sequence_number = _sequence++,
                    error,
                }),
                ResponseSseSerializer.SerializeEvent("response.failed", new
                {
                    type = "response.failed",
                    sequence_number = _sequence++,
                    response = CreateResponse(ResponseStatuses.Failed, BuildOutputItems(ResponseStatuses.Failed, GetLastOutputIndex()), error),
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

        private ResponseItem[] BuildOutputItems(string status, int? lastOutputIndex)
        {
            var items = new List<(int OutputIndex, ResponseItem Item)>();

            if (_message is not null)
            {
                items.Add((_message.OutputIndex, CreateMessageItem(
                    _message,
                    ChatCompletionsTranslator.MapOutputItemStatus(status, _message.OutputIndex == lastOutputIndex))));
            }

            foreach (var toolState in _toolCalls.Values)
            {
                items.Add((toolState.OutputIndex, CreateFunctionCallItem(
                    toolState,
                    ChatCompletionsTranslator.MapOutputItemStatus(status, toolState.OutputIndex == lastOutputIndex))));
            }

            return items
                .OrderBy(item => item.OutputIndex)
                .Select(item => item.Item)
                .ToArray();
        }

        private int? GetLastOutputIndex()
        {
            int? lastOutputIndex = _message?.OutputIndex;

            foreach (var toolState in _toolCalls.Values)
            {
                if (!lastOutputIndex.HasValue || toolState.OutputIndex > lastOutputIndex.Value)
                {
                    lastOutputIndex = toolState.OutputIndex;
                }
            }

            return lastOutputIndex;
        }

        private Response CreateResponse(string status, ResponseItem[] output, ResponseError? error) => new()
        {
            Id = ResponseId,
            Status = status,
            Model = Model ?? _requestModel,
            Output = output,
            CompletedAt = status == ResponseStatuses.Completed ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : null,
            Usage = Usage,
            Error = error,
            IncompleteDetails = ChatCompletionsTranslator.MapIncompleteDetailsForStatus(status),
            Temperature = _temperature ?? 1.0,
            TopP = _topP ?? 1.0,
            MaxOutputTokens = _maxOutputTokens,
            Tools = _tools ?? [],
            ToolChoice = (object?)_toolChoice ?? "auto",
            PreviousResponseId = _previousResponseId,
            Instructions = _instructions,
            Truncation = _truncation,
            ParallelToolCalls = _parallelToolCalls,
            Text = _text,
            PresencePenalty = _presencePenalty,
            FrequencyPenalty = _frequencyPenalty,
            TopLogprobs = _topLogprobs,
            Store = _store,
            Background = _background,
            ServiceTier = _serviceTier,
            Metadata = _metadata,
            MaxToolCalls = _maxToolCalls,
            Reasoning = _reasoning,
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
