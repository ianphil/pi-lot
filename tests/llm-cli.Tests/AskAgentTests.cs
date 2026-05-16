#pragma warning disable OPENAI001

using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class AskAgentTests
{
    [Fact]
    public async Task RunNonStreamingAsync_ExecutesFunctionCallAndReturnsFinalText()
    {
        var firstResponse = new ResponseResult
        {
            Id = "resp_1",
            Model = "gpt-5.4-mini",
            Status = ResponseStatus.Completed,
        };
        firstResponse.OutputItems.Add(ResponseItem.CreateFunctionCallItem(
            "call_1",
            FetchUrlTool.ToolName,
            BinaryData.FromString("""{"url":"https://example.com"}""")));

        var secondResponse = new ResponseResult
        {
            Id = "resp_2",
            Model = "gpt-5.4-mini",
            Status = ResponseStatus.Completed,
        };
        secondResponse.OutputItems.Add(ResponseItem.CreateAssistantMessageItem("Example summary"));

        var sentOptions = new List<CreateResponseOptions>();
        var queuedResponses = new Queue<ResponseResult>(new[] { firstResponse, secondResponse });
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");

        var agent = new AskAgent(
            (options, _) =>
            {
                sentOptions.Add(options);
                return Task.FromResult(queuedResponses.Dequeue());
            },
            (_, _) => ToAsyncEnumerable([]),
            toolRegistry,
            TextWriter.Null);

        var result = await agent.RunNonStreamingAsync(
            new AskRequest("Summarize https://example.com", "gpt-5.4-mini", null, true),
            CancellationToken.None);

        Assert.Equal("Example summary", result);
        Assert.Equal(2, sentOptions.Count);
        Assert.Single(sentOptions[0].InputItems);
        Assert.Single(sentOptions[0].Tools);

        Assert.Equal(3, sentOptions[1].InputItems.Count);
        Assert.IsType<FunctionCallResponseItem>(sentOptions[1].InputItems[1]);

        var functionOutput = Assert.IsType<FunctionCallOutputResponseItem>(sentOptions[1].InputItems[2]);
        Assert.Equal("call_1", functionOutput.CallId);
        Assert.Equal(1, toolRegistry.ExecutionCount);
    }

    [Fact]
    public async Task RunStreamingAsync_ExecutesToolCallAndPrintsFinalText()
    {
        var firstResponse = new ResponseResult
        {
            Id = "resp_1",
            Model = "gpt-5.4-mini",
            Status = ResponseStatus.Completed,
        };
        firstResponse.OutputItems.Add(ResponseItem.CreateFunctionCallItem(
            "call_1",
            FetchUrlTool.ToolName,
            BinaryData.FromString("""{"url":"https://example.com"}""")));

        var secondResponse = new ResponseResult
        {
            Id = "resp_2",
            Model = "gpt-5.4-mini",
            Status = ResponseStatus.Completed,
        };
        secondResponse.OutputItems.Add(ResponseItem.CreateAssistantMessageItem("Streamed summary"));

        var sentOptions = new List<CreateResponseOptions>();
        var streamInvocationCount = 0;
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");
        using var writer = new StringWriter();

        var agent = new AskAgent(
            (_, _) => throw new NotSupportedException(),
            (options, _) =>
            {
                sentOptions.Add(options);
                streamInvocationCount++;

                return streamInvocationCount switch
                {
                    1 => ToAsyncEnumerable(
                    [
                        new StreamingResponseOutputTextDeltaUpdate
                        {
                            ItemId = "msg_pretool",
                            OutputIndex = 0,
                            ContentIndex = 0,
                            Delta = "I'll fetch that URL for you.",
                        },
                        new StreamingResponseCompletedUpdate
                        {
                            Response = firstResponse,
                        },
                    ]),
                    2 => ToAsyncEnumerable(
                    [
                        new StreamingResponseOutputTextDeltaUpdate
                        {
                            ItemId = "msg_1",
                            OutputIndex = 0,
                            ContentIndex = 0,
                            Delta = "Streamed ",
                        },
                        new StreamingResponseOutputTextDeltaUpdate
                        {
                            ItemId = "msg_1",
                            OutputIndex = 0,
                            ContentIndex = 0,
                            Delta = "summary",
                        },
                        new StreamingResponseCompletedUpdate
                        {
                            Response = secondResponse,
                        },
                    ]),
                    _ => throw new InvalidOperationException("Unexpected extra streaming request."),
                };
            },
            toolRegistry,
            writer);

        await agent.RunStreamingAsync(
            new AskRequest("Summarize https://example.com", "gpt-5.4-mini", null, true),
            CancellationToken.None);

        Assert.Equal($"Streamed summary{Environment.NewLine}", writer.ToString());
        Assert.Equal(2, sentOptions.Count);
        Assert.Equal(3, sentOptions[1].InputItems.Count);
        Assert.Equal(1, toolRegistry.ExecutionCount);
    }

    [Fact]
    public async Task RunStreamingAsync_WithoutToolsAndNoDeltasPrintsTerminalOutput()
    {
        var response = new ResponseResult
        {
            Id = "resp_1",
            Model = "gpt-5.4-mini",
            Status = ResponseStatus.Completed,
        };
        response.OutputItems.Add(ResponseItem.CreateAssistantMessageItem("Terminal summary"));
        using var writer = new StringWriter();

        var agent = new AskAgent(
            (_, _) => throw new NotSupportedException(),
            (_, _) => ToAsyncEnumerable(
            [
                new StreamingResponseCompletedUpdate
                {
                    Response = response,
                },
            ]),
            new FakeToolRegistry("""{"ok":true}"""),
            writer);

        await agent.RunStreamingAsync(
            new AskRequest("Summarize this", "gpt-5.4-mini", null, false),
            CancellationToken.None);

        Assert.Equal($"Terminal summary{Environment.NewLine}", writer.ToString());
    }

    private static IAsyncEnumerable<StreamingResponseUpdate> ToAsyncEnumerable(
        IEnumerable<StreamingResponseUpdate> updates)
        => AsyncEnumerableHelpers.ToAsyncEnumerable(updates);
}
