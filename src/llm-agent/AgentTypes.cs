using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement? Parameters { get; }
    bool? Strict { get; }

    Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}

public sealed record AgentToolResult(string Content, bool IsError = false);

public sealed record AgentToolCallResult(
    string CallId,
    string ToolName,
    string Output,
    bool IsError);

public sealed record AgentLoopOptions
{
    public required string Model { get; init; }
    public string? Instructions { get; init; }
    public IReadOnlyList<IAgentTool> Tools { get; init; } = [];
    public int? MaxTurns { get; init; }
    public double? Temperature { get; init; }
    public ResponseReasoning? Reasoning { get; init; }
}

public abstract record AgentEvent;

public sealed record AgentStarted : AgentEvent;

public sealed record AgentEnded(AgentContext Context) : AgentEvent;

public sealed record TurnStarted : AgentEvent;

public sealed record TurnEnded(Response Response, IReadOnlyList<AgentToolCallResult> ToolResults) : AgentEvent;

public sealed record MessageStarted : AgentEvent;

public sealed record MessageDelta(ResponseStreamEvent StreamEvent) : AgentEvent;

public sealed record MessageEnded(Response Response) : AgentEvent;

public sealed record ToolExecutionStarted(string CallId, string ToolName, string Arguments) : AgentEvent;

public sealed record ToolExecutionEnded(string CallId, string ToolName, AgentToolResult Result) : AgentEvent;

public static class AgentToolExtensions
{
    public static ResponseFunctionToolDefinition ToToolDefinition(this IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ResponseFunctionToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.Parameters,
            Strict = tool.Strict,
        };
    }
}
