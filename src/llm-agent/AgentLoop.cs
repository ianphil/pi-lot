using System.Runtime.CompilerServices;
using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmAgent;

public static class AgentLoop
{
    public static async IAsyncEnumerable<AgentEvent> RunAsync(
        ILlmSdkClient client,
        string prompt,
        AgentLoopOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);

        var context = new AgentContext();
        context.AddUserMessage(prompt);

        yield return new AgentStarted();

        var turnCount = 0;
        var runStatus = AgentRunStatus.Completed;
        string? runErrorMessage = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.MaxTurns.HasValue && turnCount >= options.MaxTurns.Value)
            {
                break;
            }

            turnCount++;

            yield return new TurnStarted();

            var sdkContext = BuildContext(context, options);
            var completionOptions = BuildCompletionOptions(options);
            var request = ContextTranslator.ToCreateResponseRequest(sdkContext, completionOptions);
            var budget = options.ContextBudget is null
                ? null
                : await AgentContextBudget.EvaluateAsync(client, request, options.ContextBudget, cancellationToken);
            AgentContextBudget.ThrowIfExceeded(budget);
            if (budget?.Level is AgentContextBudgetLevel.Warning)
            {
                yield return new ContextBudgetWarning(budget);
            }

            var stream = client.StreamAsync(sdkContext, completionOptions, cancellationToken);

            yield return new MessageStarted();

            AssistantMessage? message = null;
            var messageStatus = AgentMessageStatus.Completed;
            string? messageErrorMessage = null;

            await foreach (var streamEvent in stream.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (streamEvent)
                {
                    case StreamStart:
                        break;

                    case UsageEvent usage:
                        yield return new MessageUsage(usage.Usage);
                        break;

                    case StreamDone done:
                        message = done.FinalMessage;
                        messageStatus = ToMessageStatus(done.FinalMessage.StopReason);
                        messageErrorMessage = done.FinalMessage.ErrorMessage;
                        if (done.FinalMessage.Diagnostics is not null)
                        {
                            yield return new MessageDiagnostics(done.FinalMessage.Diagnostics);
                        }

                        yield return new MessageEnded(done.FinalMessage)
                        {
                            Status = messageStatus,
                            IsPartial = IsPartial(messageStatus),
                            ErrorMessage = messageErrorMessage,
                        };
                        break;

                    case StreamError error:
                        message = error.PartialMessage;
                        messageStatus = AgentMessageStatus.FailedPartial;
                        messageErrorMessage = error.Message;
                        if (error.PartialMessage.Diagnostics is not null)
                        {
                            yield return new MessageDiagnostics(error.PartialMessage.Diagnostics);
                        }

                        yield return new MessageEnded(error.PartialMessage)
                        {
                            Status = messageStatus,
                            IsPartial = true,
                            ErrorMessage = messageErrorMessage,
                        };
                        break;

                    default:
                        yield return new MessageDelta(streamEvent);
                        break;
                }
            }

            if (message is null)
            {
                throw new InvalidOperationException("Response stream ended without a terminal assistant message event.");
            }

            context.AddAssistantMessage(message);

            var toolResults = new List<AgentToolCallResult>();
            var functionCalls = message.Content.OfType<ToolCallContent>().ToArray();

            if (messageStatus is not AgentMessageStatus.Completed)
            {
                runStatus = ToRunStatus(messageStatus);
                runErrorMessage = messageErrorMessage ?? message.ErrorMessage;
                yield return new TurnEnded(message, toolResults);
                break;
            }

            foreach (var functionCall in functionCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ToolExecutionStarted(functionCall.Id, functionCall.Name, functionCall.ArgumentsJson);

                var result = await ExecuteToolAsync(options.Tools, functionCall, cancellationToken);
                toolResults.Add(new AgentToolCallResult(functionCall.Id, functionCall.Name, result.Content, result.IsError));
                context.AddToolResult(functionCall.Id, result.Content);

                yield return new ToolExecutionEnded(functionCall.Id, functionCall.Name, result);
            }

            yield return new TurnEnded(message, toolResults);

            if (functionCalls.Length == 0)
            {
                break;
            }
        }

        yield return new AgentEnded(context)
        {
            Status = runStatus,
            ErrorMessage = runErrorMessage,
        };
    }

    private static Context BuildContext(AgentContext context, AgentLoopOptions options)
        => context.ToSdkContext(options.Instructions, options.Tools.Select(static tool => tool.ToToolDefinition()).ToArray());

    private static CompletionOptions BuildCompletionOptions(AgentLoopOptions options)
        => new()
        {
            Model = options.Model,
            Temperature = options.Temperature,
            Thinking = options.Thinking ?? ToThinkingLevel(options.Reasoning),
            Cache = options.CacheRetention,
            SessionId = options.SessionId ?? options.PromptCacheKey,
            RequestId = options.RequestId,
            CorrelationId = options.CorrelationId,
            Metadata = options.Metadata,
            TimeoutMs = options.TimeoutMs,
            MaxRetries = options.MaxRetries,
            MaxRetryDelayMs = options.MaxRetryDelayMs,
            Headers = options.Headers,
            OnPayload = options.OnPayload,
            OnResponse = options.OnResponse,
        };

    private static async Task<AgentToolResult> ExecuteToolAsync(
        IReadOnlyList<IAgentTool> tools,
        ToolCallContent functionCall,
        CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Name, functionCall.Name, StringComparison.Ordinal));
        if (tool is null)
        {
            return new AgentToolResult($"Tool '{functionCall.Name}' not found.", true);
        }

        var validation = ToolValidator.Validate(ToValidationDefinition(tool), functionCall.ArgumentsJson);
        if (!validation.IsValid)
        {
            return new AgentToolResult(FormatToolValidationError(validation), true);
        }

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(functionCall.ArgumentsJson, JsonDefaults.Web);
        }
        catch (JsonException exception)
        {
            return new AgentToolResult(FormatToolValidationError(new ToolValidationResult(false, [exception.Message])), true);
        }

        try
        {
            return await tool.ExecuteAsync(functionCall.Id, arguments, cancellationToken);
        }
        catch (Exception exception)
        {
            return new AgentToolResult(exception.Message, true);
        }
    }

    private static ToolDefinition ToValidationDefinition(IAgentTool tool) =>
        new(tool.Name, tool.Description, tool.Parameters, tool.Strict);

    private static ThinkingLevel? ToThinkingLevel(ResponseReasoning? reasoning) =>
        reasoning?.Effort?.ToLowerInvariant() switch
        {
            "minimal" => ThinkingLevel.Minimal,
            "low" => ThinkingLevel.Low,
            "medium" => ThinkingLevel.Medium,
            "high" => ThinkingLevel.High,
            "xhigh" => ThinkingLevel.XHigh,
            _ => null,
        };

    private static AgentMessageStatus ToMessageStatus(StopReason stopReason) => stopReason switch
    {
        StopReason.Length => AgentMessageStatus.Incomplete,
        StopReason.Aborted => AgentMessageStatus.Cancelled,
        StopReason.Error => AgentMessageStatus.FailedPartial,
        _ => AgentMessageStatus.Completed,
    };

    private static AgentRunStatus ToRunStatus(AgentMessageStatus messageStatus) => messageStatus switch
    {
        AgentMessageStatus.Incomplete => AgentRunStatus.Incomplete,
        AgentMessageStatus.Cancelled => AgentRunStatus.Cancelled,
        AgentMessageStatus.FailedPartial => AgentRunStatus.Failed,
        _ => AgentRunStatus.Completed,
    };

    private static bool IsPartial(AgentMessageStatus messageStatus) => messageStatus is not AgentMessageStatus.Completed;

    private static string FormatToolValidationError(ToolValidationResult result)
    {
        if (result.Errors.Count == 0)
        {
            return "Tool argument validation failed.";
        }

        return $"Tool argument validation failed: {string.Join("; ", result.Errors)}";
    }

}
