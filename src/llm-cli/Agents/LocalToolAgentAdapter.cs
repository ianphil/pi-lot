using System.Text.Json;
using LlmAgent;

namespace llm_cli.Agents;

internal sealed class LocalToolAgentAdapter(ILocalTool tool, IToolRegistry toolRegistry) : IAgentTool
{
    private readonly ILocalTool _tool = tool ?? throw new ArgumentNullException(nameof(tool));
    private readonly IToolRegistry _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));

    public string Name => _tool.Name;

    public string Description => _tool.Description;

    public JsonElement? Parameters => _tool.Parameters;

    public bool? Strict => _tool.Strict;

    public async Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        var output = await _toolRegistry.ExecuteAsync(_tool.Name, BinaryData.FromString(arguments.GetRawText()), cancellationToken);
        return new AgentToolResult(output);
    }
}
