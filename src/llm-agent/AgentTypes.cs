using System.Text.Json;
using System.Text.Json.Nodes;
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
    public ThinkingLevel? Thinking { get; init; }
    public CacheRetention CacheRetention { get; init; } = CacheRetention.None;
    public string? SessionId { get; init; }
    public string? RequestId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public int? TimeoutMs { get; init; }
    public int? MaxRetries { get; init; }
    public int? MaxRetryDelayMs { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public string? PromptCacheKey { get; init; }
    public Func<JsonNode, JsonNode?>? OnPayload { get; init; }
    public Action<ResponseSnapshot>? OnResponse { get; init; }
    public AgentContextBudgetOptions? ContextBudget { get; init; }
}

public abstract record AgentEvent;

public enum AgentRunStatus
{
    Completed,
    Incomplete,
    Cancelled,
    Failed,
}

public enum AgentMessageStatus
{
    Completed,
    Incomplete,
    Cancelled,
    FailedPartial,
}

public sealed record AgentStarted : AgentEvent;

public sealed record AgentEnded(AgentContext Context) : AgentEvent
{
    public AgentRunStatus Status { get; init; } = AgentRunStatus.Completed;
    public string? ErrorMessage { get; init; }
}

public sealed record TurnStarted : AgentEvent;

public sealed record TurnEnded(AssistantMessage Message, IReadOnlyList<AgentToolCallResult> ToolResults) : AgentEvent
{
    public AssistantMessage Response => Message;
}

public sealed record ContextBudgetWarning(AgentContextBudgetResult Result) : AgentEvent;

public sealed record MessageStarted : AgentEvent;

public sealed record MessageDelta(AssistantStreamEvent StreamEvent) : AgentEvent;

public sealed record MessageUsage(Usage Usage) : AgentEvent;

public sealed record MessageDiagnostics(Diagnostics Diagnostics) : AgentEvent;

public sealed record MessageEnded(AssistantMessage Message) : AgentEvent
{
    public AssistantMessage Response => Message;
    public AgentMessageStatus Status { get; init; } = AgentMessageStatus.Completed;
    public bool IsPartial { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record ToolExecutionStarted(string CallId, string ToolName, string Arguments) : AgentEvent;

public sealed record ToolExecutionEnded(string CallId, string ToolName, AgentToolResult Result) : AgentEvent;

public static class AgentToolExtensions
{
    public static ToolDefinition ToToolDefinition(this IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ToolDefinition(tool.Name, tool.Description, tool.Parameters, tool.Strict);
    }
}
