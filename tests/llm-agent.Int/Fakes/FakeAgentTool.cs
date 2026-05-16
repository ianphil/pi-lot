using System.Text.Json;

namespace LlmAgent.Int.Fakes;

internal sealed class FakeAgentTool : IAgentTool
{
    private readonly Func<string, JsonElement, CancellationToken, Task<AgentToolResult>> _executeAsync;

    public FakeAgentTool(
        string name,
        string description,
        JsonElement? parameters = null,
        bool? strict = null,
        Func<string, JsonElement, CancellationToken, Task<AgentToolResult>>? executeAsync = null)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
        Strict = strict;
        _executeAsync = executeAsync ?? ((_, _, _) => throw new NotSupportedException());
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement? Parameters { get; }

    public bool? Strict { get; }

    public int ExecuteCallCount { get; private set; }

    public Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        ExecuteCallCount++;
        return _executeAsync(callId, arguments, cancellationToken);
    }
}
