using System.Text.Json;
using LlmAgent;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Agents;

public static class SdkAskAgent
{
    private const int MaxToolTurns = 10;

    public static async Task RunNonStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default,
        IToolRegistry? toolRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        var (_, response) = await RunAgentLoopAsync(client, request, writer, streamOutput: false, toolRegistry, cancellationToken);
        WriteTerminalResponse(writer, response);
    }

    public static async Task RunStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default,
        IToolRegistry? toolRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        var (wroteText, response) = await RunAgentLoopAsync(client, request, writer, streamOutput: true, toolRegistry, cancellationToken);
        WriteTerminalResponse(writer, response, wroteText);
    }

    private static async Task<(bool WroteText, Response Response)> RunAgentLoopAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        bool streamOutput,
        IToolRegistry? toolRegistry,
        CancellationToken cancellationToken)
    {
        var wroteText = false;
        var currentTurnText = new StringWriter();
        Response? finalResponse = null;

        await foreach (var agentEvent in AgentLoop.RunAsync(
            client,
            request.Prompt,
            CreateOptions(request, toolRegistry),
            cancellationToken))
        {
            switch (agentEvent)
            {
                case MessageDelta { StreamEvent: OutputTextDeltaEvent delta } when streamOutput && !request.ToolsEnabled:
                    writer.Write(delta.Delta);
                    wroteText = true;
                    break;
                case MessageDelta { StreamEvent: OutputTextDeltaEvent delta } when streamOutput:
                    currentTurnText.Write(delta.Delta);
                    break;
                case MessageEnded(var response):
                    finalResponse = response;
                    break;
                case TurnEnded(var response, var toolResults) when streamOutput && request.ToolsEnabled:
                    finalResponse = response;
                    if (toolResults.Count == 0)
                    {
                        wroteText = WriteBufferedOrResponseText(writer, currentTurnText.ToString(), response, wroteText);
                    }

                    currentTurnText.GetStringBuilder().Clear();
                    break;
                case TurnEnded(var response, _):
                    finalResponse = response;
                    break;
            }
        }

        return (wroteText, finalResponse ?? throw new InvalidOperationException("Agent loop ended without a terminal response."));
    }

    private static AgentLoopOptions CreateOptions(AskRequest request, IToolRegistry? toolRegistry)
        => new()
        {
            Model = request.Model,
            Instructions = request.SystemInstructions,
            MaxTurns = MaxToolTurns,
            Tools = request.ToolsEnabled
                ? CreateAgentTools(toolRegistry)
                : [],
        };

    private static IReadOnlyList<IAgentTool> CreateAgentTools(IToolRegistry? toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        return toolRegistry.Tools.Select(tool => (IAgentTool)new LocalToolAgentAdapter(tool, toolRegistry)).ToArray();
    }

    private static bool WriteBufferedOrResponseText(TextWriter writer, string bufferedText, Response response, bool wroteText)
    {
        if (!string.IsNullOrEmpty(bufferedText))
        {
            writer.Write(bufferedText);
            return true;
        }

        if (wroteText)
        {
            return true;
        }

        var responseText = response.GetOutputText();
        if (responseText is null)
        {
            return false;
        }

        writer.Write(responseText);
        return true;
    }

    private static void WriteTerminalResponse(TextWriter writer, Response response, bool wroteText = false)
    {
        if (response.Status == ResponseStatuses.Failed)
        {
            WriteStatusLine(writer, wroteText, $"Response failed: {GetFailureMessage(response)}");
            return;
        }

        if (!wroteText)
        {
            var text = response.GetOutputText();
            writer.WriteLine(text is null ? "No output text was returned." : text);
        }
        else
        {
            writer.WriteLine();
        }

        if (response.Status == ResponseStatuses.Incomplete)
        {
            writer.WriteLine($"Response incomplete: {GetIncompleteReason(response)}");
        }
    }

    private static void WriteStatusLine(TextWriter writer, bool wroteText, string message)
    {
        if (wroteText)
        {
            writer.WriteLine();
        }

        writer.WriteLine(message);
    }

    private static string GetFailureMessage(Response response)
        => response.Error?.Message ?? response.Status;

    private static string GetIncompleteReason(Response response)
        => response.IncompleteDetails?.Reason ?? response.Status;
}
