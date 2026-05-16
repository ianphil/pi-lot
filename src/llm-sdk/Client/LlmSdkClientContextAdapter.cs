using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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

        if (ShouldThrow(options))
        {
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

        await foreach (var streamEvent in StreamAsync(client, context, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            switch (streamEvent)
            {
                case StreamDone done:
                    return done.FinalMessage;
                case StreamError error:
                    return error.PartialMessage;
            }
        }

        return new AssistantMessage([], StopReason.Stop);
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
        var enumerator = client.CreateResponseStreamAsync(request, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        IEnumerable<AssistantStreamEvent>? interrupted = null;

        try
        {
            while (true)
            {
                ResponseStreamEvent rawEvent;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    rawEvent = enumerator.Current;
                }
                catch (Exception ex) when (IsRecoverableStreamException(ex) && !ShouldThrow(options))
                {
                    interrupted = state.Interrupt(ex);
                    break;
                }

                foreach (var streamEvent in state.Apply(rawEvent))
                {
                    yield return streamEvent;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (interrupted is not null)
        {
            foreach (var streamEvent in interrupted)
            {
                yield return streamEvent;
            }

            yield break;
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
        var enumerator = client.CreateChatCompletionStreamAsync(request, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        IEnumerable<AssistantStreamEvent>? interrupted = null;

        try
        {
            while (true)
            {
                ChatCompletionChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    chunk = enumerator.Current;
                }
                catch (Exception ex) when (IsRecoverableStreamException(ex) && !ShouldThrow(options))
                {
                    interrupted = state.Interrupt(ex);
                    break;
                }

                foreach (var streamEvent in state.Apply(chunk))
                {
                    yield return streamEvent;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (interrupted is not null)
        {
            foreach (var streamEvent in interrupted)
            {
                yield return streamEvent;
            }

            yield break;
        }

        foreach (var streamEvent in state.Complete())
        {
            yield return streamEvent;
        }
    }

    private sealed class ResponseStreamState(string? fallbackModel, IReadOnlyList<ToolDefinition> tools)
    {
        private readonly Dictionary<string, ResponseFunctionCallItem> _toolCallsByItemId = [];
        private readonly Dictionary<string, StringBuilder> _toolCallArgumentsByItemId = [];
        private readonly Dictionary<string, ResponseToolCallAccumulator> _toolCallAccumulators = [];
        private readonly List<ContentBlock> _partialContent = [];
        private Usage? _usage;
        private bool _terminal;
        private bool _started;

        public IEnumerable<AssistantStreamEvent> Apply(ResponseStreamEvent rawEvent)
        {
            if (_terminal)
            {
                return [];
            }

            var events = new List<AssistantStreamEvent>();
            AddStartIfNeeded(events, rawEvent);
            UpdateUsage(rawEvent);

            switch (rawEvent)
            {
                case OutputItemAddedEvent { Item: ResponseFunctionCallItem toolCall }:
                    _toolCallsByItemId[toolCall.Id] = toolCall;
                    _toolCallAccumulators[toolCall.Id] = new ResponseToolCallAccumulator(
                        toolCall.CallId,
                        toolCall.Name,
                        toolCall.Arguments);
                    break;

                case OutputItemDoneEvent { Item: ResponseFunctionCallItem toolCall }:
                    _toolCallsByItemId[toolCall.Id] = toolCall;
                    _toolCallAccumulators[toolCall.Id] = new ResponseToolCallAccumulator(
                        toolCall.CallId,
                        toolCall.Name,
                        toolCall.Arguments);
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
                    if (toolDelta.ItemId is not null && _toolCallAccumulators.TryGetValue(toolDelta.ItemId, out var accumulator))
                    {
                        accumulator.Append(toolDelta.Delta);
                    }

                    events.Add(ToToolCallDelta(toolDelta));
                    break;

                case FunctionCallArgumentsDoneEvent toolDone:
                    if (toolDone.ItemId is not null && _toolCallAccumulators.TryGetValue(toolDone.ItemId, out var doneAccumulator))
                    {
                        doneAccumulator.Replace(toolDone.Arguments);
                    }
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
            var message = "Response stream ended before a terminal event.";
            return [new StreamError(new AssistantMessage(GetPartialContent(), StopReason.Error, _usage, message), message)];
        }

        public IEnumerable<AssistantStreamEvent> Interrupt(Exception exception)
        {
            if (_terminal)
            {
                return [];
            }

            _terminal = true;
            if (exception is OperationCanceledException)
            {
                return [new StreamDone(new AssistantMessage(GetPartialContent(), StopReason.Aborted, _usage))];
            }

            var message = GetExceptionMessage(exception);
            return [new StreamError(new AssistantMessage(GetPartialContent(), StopReason.Error, _usage, message), message)];
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

        private void UpdateUsage(ResponseStreamEvent rawEvent)
        {
            if (rawEvent is not ResponseEvent responseEvent)
            {
                return;
            }

            var usage = UsageMath.FromResponseUsage(responseEvent.Response.Usage);
            if (usage is not null)
            {
                _usage = usage;
            }
        }

        private ToolCallDelta ToToolCallDelta(FunctionCallArgumentsDeltaEvent toolDelta)
        {
            var itemId = toolDelta.ItemId ?? string.Empty;
            var parsed = ApplyToolCallArguments(itemId, toolDelta.Delta);
            if (toolDelta.ItemId is not null && _toolCallsByItemId.TryGetValue(toolDelta.ItemId, out var toolCall))
            {
                return new ToolCallDelta(toolCall.CallId, toolCall.Name, toolDelta.Delta, parsed);
            }

            return new ToolCallDelta(itemId, string.Empty, toolDelta.Delta, parsed);
        }

        private JsonElement? ApplyToolCallArguments(string itemId, string delta)
        {
            if (!_toolCallArgumentsByItemId.TryGetValue(itemId, out var arguments))
            {
                arguments = new StringBuilder();
                _toolCallArgumentsByItemId[itemId] = arguments;
            }

            arguments.Append(delta);
            return PartialJsonParser.TryParse(arguments.ToString());
        }

        private void AddTerminalEvents(List<AssistantStreamEvent> events, Response response)
        {
            _terminal = true;
            var usage = UsageMath.FromResponseUsage(response.Usage);
            if (usage is not null)
            {
                _usage = usage;
                events.Add(new UsageEvent(usage));
            }

            events.Add(new StreamDone(ContextTranslator.ToAssistantMessage(response, tools)));
        }

        private void AddError(List<AssistantStreamEvent> events, Response? response, string message)
        {
            _terminal = true;
            var usage = response is null ? _usage : UsageMath.FromResponseUsage(response.Usage) ?? _usage;
            var partial = new AssistantMessage(GetPartialContent(), StopReason.Error, usage, message);
            events.Add(new StreamError(partial, message));
        }

        private IReadOnlyList<ContentBlock> GetPartialContent()
        {
            var content = new List<ContentBlock>(_partialContent);
            content.AddRange(_toolCallAccumulators.Values.Select(static accumulator => accumulator.ToContent()));
            return content;
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
            var message = ContextTranslator.ValidateToolCalls(CreateMessage(_stopReason), tools);
            return [new StreamDone(message)];
        }

        public IEnumerable<AssistantStreamEvent> Interrupt(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return [new StreamDone(CreateMessage(StopReason.Aborted))];
            }

            var message = GetExceptionMessage(exception);
            return [new StreamError(CreateMessage(StopReason.Error, message), message)];
        }

        private AssistantMessage CreateMessage(StopReason stopReason, string? errorMessage = null)
        {
            var content = new List<ContentBlock>();
            if (_text.Length > 0)
            {
                content.Add(new TextContent(_text.ToString()));
            }

            content.AddRange(_toolCalls
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value.ToContent()));

            return new AssistantMessage(content, stopReason, _usage, errorMessage);
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
            return new ToolCallDelta(
                accumulator.Id,
                accumulator.Name,
                toolCall.Function?.Arguments ?? string.Empty,
                accumulator.ParsedArguments);
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

        public JsonElement? ParsedArguments => PartialJsonParser.TryParse(_arguments.ToString());
    }

    private sealed class ResponseToolCallAccumulator(string id, string name, string? arguments)
    {
        private readonly StringBuilder _arguments = new(arguments ?? string.Empty);

        public ToolCallContent ToContent() => new(id, name, _arguments.ToString());

        public void Append(string? arguments)
        {
            if (!string.IsNullOrEmpty(arguments))
            {
                _arguments.Append(arguments);
            }
        }

        public void Replace(string? arguments)
        {
            _arguments.Clear();
            if (!string.IsNullOrEmpty(arguments))
            {
                _arguments.Append(arguments);
            }
        }
    }

    private static bool ShouldThrow(CompletionOptions? options) => options?.AbortMode == AbortMode.Throw;

    private static bool IsRecoverableStreamException(Exception exception) =>
        exception is OperationCanceledException or HttpRequestException or IOException or JsonException or InvalidOperationException;

    private static string GetExceptionMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

    private static StopReason ToStopReason(string? finishReason) => finishReason switch
    {
        "length" => StopReason.Length,
        "tool_calls" => StopReason.ToolUse,
        "content_filter" => StopReason.ContentFilter,
        _ => StopReason.Stop,
    };
}
