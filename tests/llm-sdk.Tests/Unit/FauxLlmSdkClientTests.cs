using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Testing;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FauxLlmSdkClientTests
{
    [Fact]
    public async Task CompleteAsync_WithScriptedToolCallThenText_ReturnsAggregatedAssistantMessageAndRecordsRequests()
    {
        var client = new FauxLlmSdkClient(
        [
            FauxResponse.ToolCall("get_weather", "{\"city\":\"London\"}", id: "call_1"),
            FauxResponse.Text("It is 21C.", new Usage(10, 4)),
        ]);

        var first = await client.CompleteAsync(new Context
        {
            Messages = [new UserMessage([new TextContent("Weather?")])],
        });
        var second = await client.CompleteAsync(new Context
        {
            Messages = [new ToolMessage("call_1", [new ToolResultContent("call_1", "{\"temperature\":21}")])],
        });

        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(first.Content));
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal(StopReason.ToolUse, first.StopReason);
        Assert.Equal("It is 21C.", Assert.IsType<TextContent>(Assert.Single(second.Content)).Text);
        Assert.Equal(new Usage(10, 4), second.Usage);
        Assert.Equal(2, client.RecordedRequests.Count);
    }

    [Fact]
    public async Task StreamAsync_WithScriptedResponse_YieldsEventsInOrderAndRecordsRequest()
    {
        var scripted = FauxResponse.Text("Hello", new Usage(3, 2));
        var client = new FauxLlmSdkClient([scripted]);

        var events = await CollectAsync(client.StreamAsync(new Context
        {
            Messages = [new UserMessage([new TextContent("Hi")])],
        }));

        Assert.Collection(events,
            static e => Assert.IsType<StreamStart>(e),
            static e => Assert.Equal("Hello", Assert.IsType<TextDelta>(e).Text),
            static e => Assert.Equal(new Usage(3, 2), Assert.IsType<UsageEvent>(e).Usage),
            static e => Assert.Equal("Hello", Assert.IsType<TextContent>(Assert.Single(Assert.IsType<StreamDone>(e).FinalMessage.Content)).Text));
        Assert.Single(client.RecordedRequests);
    }

    [Fact]
    public async Task CompleteAsync_WithMissingScriptedResponse_ThrowsClearInvalidOperationException()
    {
        var client = new FauxLlmSdkClient([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new Context()));

        Assert.Equal("FauxLlmSdkClient has no scripted response for call 1.", exception.Message);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var items = new List<T>();
        await foreach (var value in values)
        {
            items.Add(value);
        }

        return items;
    }
}
