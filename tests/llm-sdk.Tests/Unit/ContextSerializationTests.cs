using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ContextSerializationTests
{
    [Fact]
    public void Context_WithEveryContentBlockSubtype_RoundTripsWithStructuralEquality()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                city = new { type = "string" },
            },
        }, JsonDefaults.Web);
        var context = new Context
        {
            System = "Be helpful.",
            Tools =
            [
                new ToolDefinition("get_weather", "Gets the weather.", parameters, Strict: true),
            ],
            Messages =
            [
                new UserMessage(
                [
                    new TextContent("What is the weather?"),
                    new ImageContent("image/png", "iVBORw0KGgo="),
                ]),
                new AssistantMessage(
                [
                    new ThinkingContent("Need a tool.", Redacted: true, Signature: "sig_123"),
                    new ToolCallContent("call_1", "get_weather", "{\"city\":\"London\"}"),
                ], StopReason.ToolUse),
                new ToolMessage("call_1",
                [
                    new ToolResultContent("call_1", "{\"temperature\":21}"),
                ]),
            ],
        };

        var json = JsonSerializer.Serialize(context, JsonDefaults.Web);

        Assert.Contains("\"role\":\"user\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"assistant\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"tool\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"image\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"thinking\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"tool_call\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"tool_result\"", json, StringComparison.Ordinal);
        Assert.Equal(context, JsonSerializer.Deserialize<Context>(json, JsonDefaults.Web));
    }
}
