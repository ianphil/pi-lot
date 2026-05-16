using System.Runtime.CompilerServices;
using System.Text;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Client;

internal static class LlmSdkClientContextAdapter
{
    public static async Task<AssistantMessage> CompleteAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);

        if (options?.PreferredApi == CompletionApi.ChatCompletions)
        {
            var chatResponse = await client.CreateChatCompletionAsync(
                ContextTranslator.ToChatCompletionRequest(context, options),
                cancellationToken);
            return ContextTranslator.ToAssistantMessage(chatResponse, context.Tools);
        }

        var response = await client.CreateResponseAsync(
            ContextTranslator.ToCreateResponseRequest(context, options),
            cancellationToken);
        return ContextTranslator.ToAssistantMessage(response, context.Tools);
    }

    public static async IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);

        if (options?.PreferredApi == CompletionApi.ChatCompletions)
        {
            await foreach (var streamEvent in StreamChatCompletionsAsync(client, context, options, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                yield return streamEvent;
            }

            yield break;
        }

        await foreach (var streamEvent in StreamResponsesAsync(client, context, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> StreamResponsesAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new ResponseStreamState(options?.Model, context.Tools);
        var request = ContextTranslator.ToCreateResponseRequest(context, options);

        await foreach (var rawEvent in client.CreateResponseStreamAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var streamEvent in state.Apply(rawEvent))
            {
                yield return streamEvent;
            }
        }

        foreach (var streamEvent in state.CompleteIfNeeded())
        {
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> StreamChatCompletionsAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new ChatStreamState(options?.Model, context.Tools);
        var request = ContextTranslator.ToChatCompletionRequest(context, options);

        await foreach (var chunk in client.CreateChatCompletionStreamAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var streamEvent in state.Apply(chunk))
            {
                yield return streamEvent;
            }
        }

        foreach (var streamEvent in state.Complete())
        {
            yield return streamEvent;
        }
    }

    private sealed class ResponseStreamState(string? fallbackModel, IReadOnlyList<ToolDefinition> tools)
    {
        private readonly Dictionary<string, ResponseFunctionCallItem> _toolCallsByItemId = [];
        private readonly List<ContentBlock> _partialContent = [];
        private bool _terminal;
        private bool _started;

        public IEnumerable<AssistantStreamEvent> Apply(ResponseStreamEvent rawEvent)
        {
            var events = new List<AssistantStreamEvent>();
            AddStartIfNeeded(events, rawEvent);

            switch (rawEvent)
            {
                case OutputItemAddedEvent { Item: ResponseFunctionCallItem toolCall }:
                    _toolCallsByItemId[toolCall.Id] = toolCall;
                    break;

                case OutputTextDeltaEvent textDelta:
                    _partialContent.Add(new TextContent(textDelta.Delta));
                    events.Add(new TextDelta(textDelta.Delta));
                    break;

                case ReasoningDeltaEvent thinkingDelta:
                    _partialContent.Add(new ThinkingContent(thinkingDelta.Delta));
                    events.Add(new ThinkingDelta(thinkingDelta.Delta));
                    break;

                case ReasoningSummaryDeltaEvent thinkingDelta:
                    _partialContent.Add(new ThinkingContent(thinkingDelta.Delta));
                    events.Add(new ThinkingDelta(thinkingDelta.Delta));
                    break;

                case FunctionCallArgumentsDeltaEvent toolDelta:
                    events.Add(ToToolCallDelta(toolDelta));
                    break;

                case ResponseCompletedEvent completed:
                    AddTerminalEvents(events, completed.Response);
                    break;

                case ResponseIncompleteEvent incomplete:
                    AddTerminalEvents(events, incomplete.Response);
                    break;

                case ResponseFailedEvent failed:
                    AddError(events, failed.Response, failed.Response.Error?.Message ?? "Response stream failed.");
                    break;

                case ErrorEvent error:
                    AddError(events, null, error.Error.Message);
                    break;
            }

            return events;
        }

        public IEnumerable<AssistantStreamEvent> CompleteIfNeeded()
        {
            if (_terminal)
            {
                return [];
            }

            _terminal = true;
            var message = ContextTranslator.ValidateToolCalls(new AssistantMessage(_partialContent, StopReason.Stop), tools);
            return [new StreamDone(message)];
        }

        private void AddStartIfNeeded(List<AssistantStreamEvent> events, ResponseStreamEvent rawEvent)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            var model = rawEvent switch
            {
                ResponseEvent response => response.Response.Model,
                _ => fallbackModel,
            };
            events.Add(new StreamStart(model ?? string.Empty));
        }

        private ToolCallDelta ToToolCallDelta(FunctionCallArgumentsDeltaEvent toolDelta)
        {
            if (toolDelta.ItemId is not null && _toolCallsByItemId.TryGetValue(toolDelta.ItemId, out var toolCall))
            {
                return new ToolCallDelta(toolCall.CallId, toolCall.Name, toolDelta.Delta);
            }

            return new ToolCallDelta(toolDelta.ItemId ?? string.Empty, string.Empty, toolDelta.Delta);
        }

        private void AddTerminalEvents(List<AssistantStreamEvent> events, Response response)
        {
            _terminal = true;
            var usage = UsageMath.FromResponseUsage(response.Usage);
            if (usage is not null)
            {
                events.Add(new UsageEvent(usage));
            }

            events.Add(new StreamDone(ContextTranslator.ToAssistantMessage(response, tools)));
        }

        private void AddError(List<AssistantStreamEvent> events, Response? response, string message)
        {
            _terminal = true;
            var partial = response is null
                ? new AssistantMessage(_partialContent, StopReason.Error, ErrorMessage: message)
                : ContextTranslator.ToAssistantMessage(response, tools);
            events.Add(new StreamError(partial, message));
        }
    }

    private sealed class ChatStreamState(string? fallbackModel, IReadOnlyList<ToolDefinition> tools)
    {
        private readonly StringBuilder _text = new();
        private readonly Dictionary<int, ToolCallAccumulator> _toolCalls = [];
        private string? _model = fallbackModel;
        private StopReason _stopReason = StopReason.Stop;
        private Usage? _usage;
        private bool _started;

        public IEnumerable<AssistantStreamEvent> Apply(ChatCompletionChunk chunk)
        {
            var events = new List<AssistantStreamEvent>();
            AddStartIfNeeded(events, chunk.Model);

            var usage = UsageMath.FromUsageInfo(chunk.Usage);
            if (usage is not null)
            {
                _usage = usage;
                events.Add(new UsageEvent(usage));
            }

            foreach (var choice in chunk.Choices ?? [])
            {
                if (!string.IsNullOrEmpty(choice.Delta?.Content))
                {
                    _text.Append(choice.Delta.Content);
                    events.Add(new TextDelta(choice.Delta.Content!));
                }

                if (choice.Delta?.ToolCalls is not null)
                {
                    foreach (var toolCall in choice.Delta.ToolCalls)
                    {
                        events.Add(ApplyToolCall(toolCall));
                    }
                }

                if (!string.IsNullOrWhiteSpace(choice.FinishReason))
                {
                    _stopReason = ToStopReason(choice.FinishReason);
                }
            }

            return events;
        }

        public IEnumerable<AssistantStreamEvent> Complete()
        {
            var content = new List<ContentBlock>();
            if (_text.Length > 0)
            {
                content.Add(new TextContent(_text.ToString()));
            }

            content.AddRange(_toolCalls
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value.ToContent()));

            var message = new AssistantMessage(content, _stopReason, _usage);
            return [new StreamDone(ContextTranslator.ValidateToolCalls(message, tools))];
        }

        private void AddStartIfNeeded(List<AssistantStreamEvent> events, string? model)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _model = model ?? _model;
            events.Add(new StreamStart(_model ?? string.Empty));
        }

        private ToolCallDelta ApplyToolCall(ChatChunkToolCall toolCall)
        {
            var index = toolCall.Index ?? 0;
            if (!_toolCalls.TryGetValue(index, out var accumulator))
            {
                accumulator = new ToolCallAccumulator();
                _toolCalls[index] = accumulator;
            }

            accumulator.Apply(toolCall);
            return new ToolCallDelta(accumulator.Id, accumulator.Name, toolCall.Function?.Arguments ?? string.Empty);
        }
    }

    private sealed class ToolCallAccumulator
    {
        private readonly StringBuilder _arguments = new();

        public string Id { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;

        public void Apply(ChatChunkToolCall toolCall)
        {
            if (!string.IsNullOrWhiteSpace(toolCall.Id))
            {
                Id = toolCall.Id!;
            }

            if (!string.IsNullOrWhiteSpace(toolCall.Function?.Name))
            {
                Name = toolCall.Function.Name!;
            }

            if (!string.IsNullOrEmpty(toolCall.Function?.Arguments))
            {
                _arguments.Append(toolCall.Function.Arguments);
            }
        }

        public ToolCallContent ToContent() => new(Id, Name, _arguments.ToString());
    }

    private static StopReason ToStopReason(string? finishReason) => finishReason switch
    {
        "length" => StopReason.Length,
        "tool_calls" => StopReason.ToolUse,
        "content_filter" => StopReason.ContentFilter,
        _ => StopReason.Stop,
    };
}
