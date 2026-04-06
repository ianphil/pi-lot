using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Unit;

public sealed class AgentToolTests
{
    [Fact]
    public void ToToolDefinition_MapsAgentToolProperties()
    {
        var parameters = JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                properties = new
                {
                    city = new
                    {
                        type = "string",
                    },
                },
            },
            JsonDefaults.Web);
        var tool = new InlineAgentTool(parameters);

        var definition = tool.ToToolDefinition();

        Assert.Equal("lookup_weather", definition.Name);
        Assert.Equal("Look up weather information.", definition.Description);
        Assert.True(definition.Parameters.HasValue);
        Assert.Equal("object", definition.Parameters.Value.GetProperty("type").GetString());
        Assert.True(definition.Strict);
    }

    private sealed class InlineAgentTool(JsonElement parameters) : IAgentTool
    {
        public string Name => "lookup_weather";

        public string Description => "Look up weather information.";

        public JsonElement? Parameters => parameters;

        public bool? Strict => true;

        public Task<AgentToolResult> ExecuteAsync(
            string callId,
            JsonElement arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolResult("unused"));
    }
}
