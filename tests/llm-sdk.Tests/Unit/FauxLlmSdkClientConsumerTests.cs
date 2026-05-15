using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Tests.Fakes;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FauxLlmSdkClientConsumerTests
{
    [Fact]
    public async Task ScriptedResponses_DriveConsumerToolLoopWithoutProxy()
    {
        var client = new FauxLlmSdkClient(
        [
            FauxResponse.ToolCall("get_weather", "{\"city\":\"London\"}", id: "call_1"),
            FauxResponse.Text("It is 21C.", new Usage(12, 5)),
        ]);

        var answer = await AnswerWeatherQuestionAsync(client, "Weather in London?");

        Assert.Equal("It is 21C.", answer.Text);
        Assert.Equal(new Usage(12, 5), answer.Usage);
        Assert.Collection(client.RecordedRequests,
            static request =>
            {
                var message = Assert.IsType<UserMessage>(Assert.Single(request.Messages));
                Assert.Equal("Weather in London?", Assert.IsType<TextContent>(Assert.Single(message.Content)).Text);
            },
            static request =>
            {
                var message = Assert.IsType<ToolMessage>(Assert.Single(request.Messages));
                Assert.Equal("call_1", message.ToolCallId);
                Assert.Equal("{\"temperature\":21}", Assert.IsType<ToolResultContent>(Assert.Single(message.Content)).Output);
            });
    }

    private static async Task<WeatherAnswer> AnswerWeatherQuestionAsync(ILlmSdkClient client, string question)
    {
        var first = await client.CompleteAsync(new Context
        {
            Messages = [new UserMessage([new TextContent(question)])],
        });

        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(first.Content));
        Assert.Equal("get_weather", toolCall.Name);

        var second = await client.CompleteAsync(new Context
        {
            Messages =
            [
                new ToolMessage(toolCall.Id, [new ToolResultContent(toolCall.Id, "{\"temperature\":21}")]),
            ],
        });

        return new WeatherAnswer(
            Assert.IsType<TextContent>(Assert.Single(second.Content)).Text,
            second.Usage);
    }

    private sealed record WeatherAnswer(string Text, Usage? Usage);
}
