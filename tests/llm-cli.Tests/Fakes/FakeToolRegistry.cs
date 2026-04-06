#pragma warning disable OPENAI001

using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli.Tests.Fakes;

internal sealed class FakeToolRegistry(string output) : IToolRegistry
{
    private static readonly JsonElement s_Parameters = JsonSerializer.SerializeToElement(new
    {
        type = "object",
    });
    private static readonly ResponseTool s_ToolDefinition = ResponseTool.CreateFunctionTool(
        functionName: FetchUrlTool.ToolName,
        functionParameters: BinaryData.FromString(s_Parameters.GetRawText()),
        strictModeEnabled: true,
        functionDescription: "test");

    private static readonly ChatTool s_ChatToolDefinition = ChatTool.CreateFunctionTool(
        functionName: FetchUrlTool.ToolName,
        functionParameters: BinaryData.FromString(s_Parameters.GetRawText()),
        functionDescription: "test");

    public int ExecutionCount { get; private set; }

    public IReadOnlyList<ILocalTool> Tools { get; } = [new FakeLocalTool()];

    public IReadOnlyList<ResponseTool> Definitions { get; } = [s_ToolDefinition];

    public IReadOnlyList<ChatTool> ChatDefinitions { get; } = [s_ChatToolDefinition];

    public Task<string> ExecuteAsync(string toolName, BinaryData arguments, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        return Task.FromResult(output);
    }

    private sealed class FakeLocalTool : ILocalTool
    {
        public string Name => FetchUrlTool.ToolName;

        public string Description => "test";

        public JsonElement? Parameters => s_Parameters;

        public bool? Strict => true;

        public ResponseTool Definition => s_ToolDefinition;

        public ChatTool ChatDefinition => s_ChatToolDefinition;

        public Task<string> ExecuteAsync(BinaryData arguments, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
