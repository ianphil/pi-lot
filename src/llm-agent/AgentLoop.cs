using System.Runtime.CompilerServices;
using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;

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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.MaxTurns.HasValue && turnCount >= options.MaxTurns.Value)
            {
                break;
            }

            turnCount++;

            yield return new TurnStarted();

            var request = BuildRequest(context, options);
            var budget = options.ContextBudget is null
                ? null
                : await AgentContextBudget.EvaluateAsync(client, request, options.ContextBudget, cancellationToken);
            AgentContextBudget.ThrowIfExceeded(budget);
            if (budget?.Level is AgentContextBudgetLevel.Warning)
            {
                yield return new ContextBudgetWarning(budget);
            }

            var stream = client.CreateResponseStreamAsync(request, cancellationToken);

            yield return new MessageStarted();

            Response? response = null;
            var terminalState = StreamTerminalState.Completed;

            await foreach (var streamEvent in stream.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (streamEvent)
                {
                    case ResponseCompletedEvent completed:
                        response = completed.Response;
                        terminalState = StreamTerminalState.Completed;
                        yield return new MessageEnded(completed.Response);
                        break;

                    case ResponseFailedEvent failed:
                        response = failed.Response;
                        terminalState = StreamTerminalState.Failed;
                        yield return new MessageEnded(failed.Response);
                        break;

                    case ResponseIncompleteEvent incomplete:
                        response = incomplete.Response;
                        terminalState = StreamTerminalState.Incomplete;
                        yield return new MessageEnded(incomplete.Response);
                        break;

                    default:
                        yield return new MessageDelta(streamEvent);
                        break;
                }
            }

            if (response is null)
            {
                throw new InvalidOperationException("Response stream ended without a terminal response event.");
            }

            context.AddResponseOutput(response.Output);

            var toolResults = new List<AgentToolCallResult>();
            var functionCalls = response.Output.OfType<ResponseFunctionCallItem>().ToArray();

            if (terminalState is not StreamTerminalState.Completed)
            {
                yield return new TurnEnded(response, toolResults);
                break;
            }

            foreach (var functionCall in functionCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ToolExecutionStarted(functionCall.CallId, functionCall.Name, functionCall.Arguments);

                var result = await ExecuteToolAsync(options.Tools, functionCall, cancellationToken);
                toolResults.Add(new AgentToolCallResult(functionCall.CallId, functionCall.Name, result.Content, result.IsError));
                context.AddToolResult(functionCall.CallId, result.Content);

                yield return new ToolExecutionEnded(functionCall.CallId, functionCall.Name, result);
            }

            yield return new TurnEnded(response, toolResults);

            if (functionCalls.Length == 0)
            {
                break;
            }
        }

        yield return new AgentEnded(context);
    }

    private static CreateResponseRequest BuildRequest(AgentContext context, AgentLoopOptions options)
        => new()
        {
            Model = options.Model,
            Input = context.SerializeInput(),
            Stream = true,
            Instructions = options.Instructions,
            Temperature = options.Temperature,
            Reasoning = options.Reasoning,
            RequestId = options.RequestId,
            CorrelationId = options.CorrelationId,
            Metadata = options.Metadata,
            TimeoutMs = options.TimeoutMs,
            MaxRetries = options.MaxRetries,
            MaxRetryDelayMs = options.MaxRetryDelayMs,
            Headers = options.Headers,
            PromptCacheKey = options.PromptCacheKey,
            OnPayload = options.OnPayload,
            OnResponse = options.OnResponse,
            Tools = options.Tools.Select(static tool => tool.ToToolDefinition()).ToArray(),
        };

    private static async Task<AgentToolResult> ExecuteToolAsync(
        IReadOnlyList<IAgentTool> tools,
        ResponseFunctionCallItem functionCall,
        CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Name, functionCall.Name, StringComparison.Ordinal));
        if (tool is null)
        {
            return new AgentToolResult($"Tool '{functionCall.Name}' not found.", true);
        }

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(functionCall.Arguments, JsonDefaults.Web);
        }
        catch (JsonException exception)
        {
            return new AgentToolResult($"Invalid arguments: {exception.Message}", true);
        }

        try
        {
            return await tool.ExecuteAsync(functionCall.CallId, arguments, cancellationToken);
        }
        catch (Exception exception)
        {
            return new AgentToolResult(exception.Message, true);
        }
    }

    private enum StreamTerminalState
    {
        Completed,
        Failed,
        Incomplete,
    }
}
