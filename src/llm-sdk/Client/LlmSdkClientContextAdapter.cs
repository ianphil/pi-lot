using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmSdk.Core;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmSdk.Client;

internal static class LlmSdkClientContextAdapter
{
    public static async Task<AssistantMessage> CompleteAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);
        logger ??= NullLogger.Instance;
        var diagnostics = new DiagnosticsBuilder();

        if (ShouldThrow(options))
        {
            var effectiveOptions = await ApplyThinkingClampAsync(client, options, diagnostics, cancellationToken);
            context = await PrepareContextAsync(client, context, effectiveOptions, logger, diagnostics, cancellationToken);
            if (effectiveOptions?.PreferredApi == CompletionApi.ChatCompletions)
            {
                var chatResponse = await client.CreateChatCompletionAsync(
                    ContextTranslator.ToChatCompletionRequest(context, effectiveOptions),
                    cancellationToken);
                return await AttachDiagnosticsAsync(
                    client,
                    ContextTranslator.ToAssistantMessage(chatResponse, context.Tools),
                    effectiveOptions,
                    diagnostics,
                    cancellationToken);
            }

            var response = await client.CreateResponseAsync(
                ContextTranslator.ToCreateResponseRequest(context, effectiveOptions),
                cancellationToken);
            return await AttachDiagnosticsAsync(
                client,
                ContextTranslator.ToAssistantMessage(response, context.Tools),
                effectiveOptions,
                diagnostics,
                cancellationToken);
        }

        await foreach (var streamEvent in StreamAsync(client, context, options, logger, diagnostics, cancellationToken)
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

        return AttachDiagnostics(new AssistantMessage([], StopReason.Stop), diagnostics);
    }

    public static async IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        ILogger? logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);
        logger ??= NullLogger.Instance;
        var diagnostics = new DiagnosticsBuilder();

        await foreach (var streamEvent in StreamAsync(client, context, options, logger, diagnostics, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        ILogger? logger,
        DiagnosticsBuilder diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger ??= NullLogger.Instance;
        var effectiveOptions = await ApplyThinkingClampAsync(client, options, diagnostics, cancellationToken);
        context = await PrepareContextAsync(client, context, effectiveOptions, logger, diagnostics, cancellationToken);

        if (effectiveOptions?.PreferredApi == CompletionApi.ChatCompletions)
        {
            await foreach (var streamEvent in StreamChatCompletionsAsync(client, context, effectiveOptions, diagnostics, cancellationToken)
                                .WithCancellation(cancellationToken))
            {
                yield return streamEvent;
            }

            yield break;
        }

        await foreach (var streamEvent in StreamResponsesAsync(client, context, effectiveOptions, diagnostics, cancellationToken)
                            .WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }
    }

    public static Task<AssistantMessage> CompleteAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        CancellationToken cancellationToken) =>
        CompleteAsync(client, context, options, NullLogger.Instance, cancellationToken);

    public static IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        CancellationToken cancellationToken) =>
        StreamAsync(client, context, options, NullLogger.Instance, cancellationToken);

    private static async Task<CompletionOptions?> ApplyThinkingClampAsync(
        ILlmSdkClient client,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        CancellationToken cancellationToken)
    {
        if (options?.Thinking is not { } requested || string.IsNullOrWhiteSpace(options.Model))
        {
            return options;
        }

        var model = await client.GetModelAsync(options.Model, cancellationToken);
        var clamped = ThinkingLevelClamp.Clamp(requested, model);
        if (clamped != options.Thinking)
        {
            diagnostics.Add(
                DiagnosticSeverity.Warning,
                "thinking_clamped",
                "Requested thinking level was clamped to the model-supported level.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["model"] = model.Id,
                    ["requested"] = requested.ToString(),
                    ["effective"] = clamped?.ToString() ?? "none",
                });
        }

        return clamped == options.Thinking
            ? options
            : options with { Thinking = clamped };
    }

    private static async Task<Context> PrepareContextAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        ILogger logger,
        DiagnosticsBuilder diagnostics,
        CancellationToken cancellationToken)
    {
        var imageCount = CountImages(context);
        if (imageCount == 0 || string.IsNullOrWhiteSpace(options?.Model))
        {
            return context;
        }

        var model = await client.GetModelAsync(options.Model, cancellationToken);
        if (model.SupportsVision)
        {
            return context;
        }

        logger.LogDebug(
            LogEvents.ImagesDroppedForNonVisionModel,
            "Dropping {ImageCount} image(s) for non-vision model {Model}.",
            imageCount,
            model.Id);
        diagnostics.Add(
            DiagnosticSeverity.Warning,
            "image_dropped",
            "Image content was omitted because the selected model does not support vision.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = imageCount.ToString(CultureInfo.InvariantCulture),
                ["model"] = model.Id,
            });
        return DropImages(context);
    }

    private static int CountImages(Context context) =>
        context.Messages.Sum(static message => GetContent(message).Count(static block => block is ImageContent));

    private static Context DropImages(Context context) => context with
    {
        Messages = context.Messages.Select(DropImages).ToArray(),
    };

    private static Message DropImages(Message message) => message switch
    {
        UserMessage user => user with { Content = DropImages(user.Content) },
        AssistantMessage assistant => assistant with { Content = DropImages(assistant.Content) },
        ToolMessage tool => tool with { Content = DropImages(tool.Content) },
        _ => message,
    };

    private static IReadOnlyList<ContentBlock> DropImages(IReadOnlyList<ContentBlock> content) =>
        content.Select(static block => block is ImageContent
            ? new TextContent("[image omitted: model does not support vision]")
            : block).ToArray();

    private static IReadOnlyList<ContentBlock> GetContent(Message message) => message switch
    {
        UserMessage user => user.Content,
        AssistantMessage assistant => assistant.Content,
        ToolMessage tool => tool.Content,
        _ => [],
    };

    private static async IAsyncEnumerable<AssistantStreamEvent> StreamResponsesAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new ResponseStreamState(options?.Model, context.Tools, diagnostics);
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
                    yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
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
                yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
            }

            yield break;
        }

        foreach (var streamEvent in state.CompleteIfNeeded())
        {
            yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
        }
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> StreamChatCompletionsAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new ChatStreamState(options?.Model, context.Tools, diagnostics);
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
                    yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
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
                yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
            }

            yield break;
        }

        foreach (var streamEvent in state.Complete())
        {
            yield return await AttachDiagnosticsAsync(client, streamEvent, options, diagnostics, cancellationToken);
        }
    }

    private sealed class ResponseStreamState(
        string? fallbackModel,
        IReadOnlyList<ToolDefinition> tools,
        DiagnosticsBuilder diagnostics)
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
            AddPartialDueToError(typeof(InvalidOperationException).Name);
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
                diagnostics.Add(
                    DiagnosticSeverity.Info,
                    "partial_due_to_abort",
                    "A partial assistant message was returned because the request was aborted.");
                return [new StreamDone(new AssistantMessage(GetPartialContent(), StopReason.Aborted, _usage))];
            }

            var message = GetExceptionMessage(exception);
            if (exception is ContextOverflowException overflow)
            {
                AddOverflowDetected(overflow);
            }
            else
            {
                AddPartialDueToError(exception.GetType().Name);
            }

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
            AddPartialDueToError("upstream");
            var partial = new AssistantMessage(GetPartialContent(), StopReason.Error, usage, message);
            events.Add(new StreamError(partial, message));
        }

        private void AddPartialDueToError(string exception)
        {
            diagnostics.Add(
                DiagnosticSeverity.Error,
                "partial_due_to_error",
                "A partial assistant message was returned because the stream failed.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["exception"] = exception,
                });
        }

        private void AddOverflowDetected(ContextOverflowException overflow)
        {
            diagnostics.Add(
                DiagnosticSeverity.Error,
                "overflow_detected",
                "The request exceeded the model context window.",
                CreateOverflowDetail(overflow));
        }

        private IReadOnlyList<ContentBlock> GetPartialContent()
        {
            var content = new List<ContentBlock>(_partialContent);
            content.AddRange(_toolCallAccumulators.Values.Select(static accumulator => accumulator.ToContent()));
            return content;
        }
    }

    private sealed class ChatStreamState(
        string? fallbackModel,
        IReadOnlyList<ToolDefinition> tools,
        DiagnosticsBuilder diagnostics)
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
                diagnostics.Add(
                    DiagnosticSeverity.Info,
                    "partial_due_to_abort",
                    "A partial assistant message was returned because the request was aborted.");
                return [new StreamDone(CreateMessage(StopReason.Aborted))];
            }

            var message = GetExceptionMessage(exception);
            if (exception is ContextOverflowException overflow)
            {
                AddOverflowDetected(overflow);
            }
            else
            {
                AddPartialDueToError(exception.GetType().Name);
            }

            return [new StreamError(CreateMessage(StopReason.Error, message), message)];
        }

        private void AddPartialDueToError(string exception)
        {
            diagnostics.Add(
                DiagnosticSeverity.Error,
                "partial_due_to_error",
                "A partial assistant message was returned because the stream failed.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["exception"] = exception,
                });
        }

        private void AddOverflowDetected(ContextOverflowException overflow)
        {
            diagnostics.Add(
                DiagnosticSeverity.Error,
                "overflow_detected",
                "The request exceeded the model context window.",
                CreateOverflowDetail(overflow));
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
        exception is OperationCanceledException or HttpRequestException or IOException or JsonException or InvalidOperationException or ContextOverflowException;

    private static async ValueTask<AssistantStreamEvent> AttachDiagnosticsAsync(
        ILlmSdkClient client,
        AssistantStreamEvent streamEvent,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        CancellationToken cancellationToken) =>
        streamEvent switch
        {
            StreamDone done => new StreamDone(await AttachDiagnosticsAsync(client, done.FinalMessage, options, diagnostics, cancellationToken)),
            StreamError error => new StreamError(await AttachDiagnosticsAsync(client, error.PartialMessage, options, diagnostics, cancellationToken), error.Message),
            _ => streamEvent,
        };

    private static async Task<AssistantMessage> AttachDiagnosticsAsync(
        ILlmSdkClient client,
        AssistantMessage message,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        CancellationToken cancellationToken)
    {
        await AddSilentTruncationIfSuspectedAsync(client, message, options, diagnostics, cancellationToken);
        return AttachDiagnostics(message, diagnostics);
    }

    private static async Task AddSilentTruncationIfSuspectedAsync(
        ILlmSdkClient client,
        AssistantMessage message,
        CompletionOptions? options,
        DiagnosticsBuilder diagnostics,
        CancellationToken cancellationToken)
    {
        if (message.StopReason != StopReason.Length ||
            message.Usage?.InputTokens is not > 0 ||
            string.IsNullOrWhiteSpace(options?.Model))
        {
            return;
        }

        var model = await client.GetModelAsync(options.Model, cancellationToken);
        if (!OverflowDetector.IsSilentTruncation(message.Usage.InputTokens, model.ContextWindow, message.StopReason))
        {
            return;
        }

        diagnostics.Add(
            DiagnosticSeverity.Warning,
            "silent_truncation_suspected",
            "The response stopped due to length near the model context window.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = model.Id,
                ["inputTokens"] = message.Usage.InputTokens.ToString(CultureInfo.InvariantCulture),
                ["window"] = model.ContextWindow?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
            });
    }

    private static AssistantMessage AttachDiagnostics(AssistantMessage message, DiagnosticsBuilder diagnostics) =>
        diagnostics.Build() is { } built
            ? message with { Diagnostics = built }
            : message;

    private static IReadOnlyDictionary<string, string> CreateOverflowDetail(ContextOverflowException overflow)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal);
        if (overflow.ContextWindow is { } contextWindow)
        {
            detail["window"] = contextWindow.ToString(CultureInfo.InvariantCulture);
        }

        if (overflow.InputTokens is { } inputTokens)
        {
            detail["input"] = inputTokens.ToString(CultureInfo.InvariantCulture);
        }

        return detail;
    }

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
