using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Unit;

public sealed class AgentContextTests
{
    [Fact]
    public void SerializeInput_WithSingleUserMessage_ProducesExpectedJsonArray()
    {
        var context = new AgentContext();

        context.AddUserMessage("Hello from the user");

        var input = context.SerializeInput();

        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.Equal(1, input.GetArrayLength());

        var item = input[0];
        Assert.Equal("message", item.GetProperty("type").GetString());
        Assert.Equal("user", item.GetProperty("role").GetString());

        var content = item.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("Hello from the user", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public void SerializeInput_WithUserMessageResponseOutputAndToolResult_ProducesExpectedItemSequence()
    {
        var context = new AgentContext();

        context.AddUserMessage("Read the file");
        context.AddResponseOutput(
        [
            new ResponseMessageItem
            {
                Id = "msg_123",
                Content =
                [
                    new ResponseOutputTextPart
                    {
                        Text = "I'll read it.",
                    },
                ],
            },
            new ResponseFunctionCallItem
            {
                Id = "fc_123",
                CallId = "call_123",
                Name = "read_file",
                Arguments = "{\"path\":\"test.txt\"}",
            },
        ]);
        context.AddToolResult("call_123", "file contents");

        var input = context.SerializeInput();

        Assert.Equal(4, input.GetArrayLength());
        Assert.Equal("message", input[0].GetProperty("type").GetString());
        Assert.Equal("message", input[1].GetProperty("type").GetString());
        Assert.Equal("function_call", input[2].GetProperty("type").GetString());
        Assert.Equal("function_call_output", input[3].GetProperty("type").GetString());
        Assert.Equal("call_123", input[3].GetProperty("call_id").GetString());
        Assert.Equal("file contents", input[3].GetProperty("output").GetString());
    }

    [Fact]
    public void SerializeInput_WithToolResult_IncludesCallIdAndOutputWithoutId()
    {
        var context = new AgentContext();

        context.AddToolResult("call_456", "done");

        var input = context.SerializeInput();

        Assert.Equal(1, input.GetArrayLength());

        var item = input[0];
        Assert.Equal("function_call_output", item.GetProperty("type").GetString());
        Assert.False(item.TryGetProperty("id", out _));
        Assert.Equal("call_456", item.GetProperty("call_id").GetString());
        Assert.Equal("done", item.GetProperty("output").GetString());
    }
}
