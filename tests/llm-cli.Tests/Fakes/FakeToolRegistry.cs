#pragma warning disable OPENAI001

using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli.Tests.Fakes;

internal sealed class FakeToolRegistry(string output) : IToolRegistry
{
    private static readonly ResponseTool s_ToolDefinition = ResponseTool.CreateFunctionTool(
        functionName: FetchUrlTool.ToolName,
        functionParameters: BinaryData.FromString("""{"type":"object"}"""),
        strictModeEnabled: true,
        functionDescription: "test");

    private static readonly ChatTool s_ChatToolDefinition = ChatTool.CreateFunctionTool(
        functionName: FetchUrlTool.ToolName,
        functionParameters: BinaryData.FromString("""{"type":"object"}"""),
        functionDescription: "test");

    public int ExecutionCount { get; private set; }

    public IReadOnlyList<ResponseTool> Definitions { get; } = [s_ToolDefinition];

    public IReadOnlyList<ChatTool> ChatDefinitions { get; } = [s_ChatToolDefinition];

    public Task<string> ExecuteAsync(string toolName, BinaryData arguments, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        return Task.FromResult(output);
    }
}
