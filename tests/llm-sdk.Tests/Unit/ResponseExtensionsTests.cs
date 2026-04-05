using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ResponseExtensionsTests
{
    [Fact]
    public void GetOutputText_WhenFirstMessageItemContainsOutputText_ReturnsText()
    {
        var response = new Response
        {
            Id = "resp_123",
            Output =
            [
                new ResponseFunctionCallItem
                {
                    Id = "fc_123",
                    CallId = "call_123",
                    Name = "search",
                },
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "Hello from Copilot",
                        },
                    ],
                },
            ],
        };

        var text = response.GetOutputText();

        Assert.Equal("Hello from Copilot", text);
    }

    [Fact]
    public void GetOutputText_WhenNoMessageItemsExist_ReturnsNull()
    {
        var response = new Response
        {
            Id = "resp_123",
            Output =
            [
                new ResponseFunctionCallItem
                {
                    Id = "fc_123",
                    CallId = "call_123",
                    Name = "search",
                },
            ],
        };

        var text = response.GetOutputText();

        Assert.Null(text);
    }

    [Fact]
    public void GetOutputText_WhenFirstMessageItemHasNoOutputTextParts_ReturnsNull()
    {
        var response = new Response
        {
            Id = "resp_123",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseInputTextPart
                        {
                            Text = "User input",
                        },
                    ],
                },
            ],
        };

        var text = response.GetOutputText();

        Assert.Null(text);
    }
}
