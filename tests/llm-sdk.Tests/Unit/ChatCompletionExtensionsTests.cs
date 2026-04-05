using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ChatCompletionExtensionsTests
{
    [Fact]
    public void GetMessageText_WhenFirstChoiceContainsMessageContent_ReturnsText()
    {
        var response = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Content = JsonDocument.Parse("\"Hello from chat\"").RootElement.Clone(),
                    },
                },
            ],
        };

        var text = response.GetMessageText();

        Assert.Equal("Hello from chat", text);
    }

    [Fact]
    public void GetMessageText_WhenNoChoicesExist_ReturnsNull()
    {
        var response = new ChatCompletionResponse
        {
            Choices = [],
        };

        var text = response.GetMessageText();

        Assert.Null(text);
    }
}
