#pragma warning disable OPENAI001

using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class ChatAgentTests
{
    [Fact]
    public async Task RunNonStreamingAsync_ReturnsFinalText()
    {
        var completion = OpenAIChatModelFactory.ChatCompletion(
            content: [ChatMessageContentPart.CreateTextPart("Hello from chat")],
            finishReason: ChatFinishReason.Stop,
            model: "gpt-5-mini");

        var agent = new ChatAgent(
            (_, _, _) => Task.FromResult(completion),
            (_, _, _) => ToAsyncEnumerable([]),
            new FakeToolRegistry(""),
            TextWriter.Null);

        var result = await agent.RunNonStreamingAsync(
            new AskRequest("Hi", "gpt-5-mini", null, false),
            CancellationToken.None);

        Assert.Equal("Hello from chat", result);
    }

    [Fact]
    public async Task RunStreamingAsync_PrintsStreamedText()
    {
        using var writer = new StringWriter();

        var agent = new ChatAgent(
            (_, _, _) => throw new NotSupportedException(),
            (_, _, _) => ToAsyncEnumerable(
            [
                OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                    contentUpdate: [ChatMessageContentPart.CreateTextPart("Hello ")]),
                OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                    contentUpdate: [ChatMessageContentPart.CreateTextPart("world")]),
                OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                    finishReason: ChatFinishReason.Stop),
            ]),
            new FakeToolRegistry(""),
            writer);

        await agent.RunStreamingAsync(
            new AskRequest("Hi", "gpt-5-mini", null, false),
            CancellationToken.None);

        Assert.Equal($"Hello world{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public async Task RunNonStreamingAsync_ExecutesToolCallAndReturnsFinalText()
    {
        var toolCallCompletion = OpenAIChatModelFactory.ChatCompletion(
            finishReason: ChatFinishReason.ToolCalls,
            model: "gpt-5-mini",
            role: ChatMessageRole.Assistant,
            toolCalls:
            [
                ChatToolCall.CreateFunctionToolCall(
                    "call_1",
                    FetchUrlTool.ToolName,
                    BinaryData.FromString("""{"url":"https://example.com"}""")),
            ]);

        var finalCompletion = OpenAIChatModelFactory.ChatCompletion(
            content: [ChatMessageContentPart.CreateTextPart("Example summary")],
            finishReason: ChatFinishReason.Stop,
            model: "gpt-5-mini");

        var invocationCount = 0;
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");

        var agent = new ChatAgent(
            (messages, _, _) =>
            {
                invocationCount++;
                return Task.FromResult(invocationCount == 1 ? toolCallCompletion : finalCompletion);
            },
            (_, _, _) => ToAsyncEnumerable([]),
            toolRegistry,
            TextWriter.Null);

        var result = await agent.RunNonStreamingAsync(
            new AskRequest("Summarize https://example.com", "gpt-5-mini", null, true),
            CancellationToken.None);

        Assert.Equal("Example summary", result);
        Assert.Equal(2, invocationCount);
        Assert.Equal(1, toolRegistry.ExecutionCount);
    }

    [Fact]
    public async Task RunStreamingAsync_ExecutesToolCallAndPrintsFinalText()
    {
        var streamInvocationCount = 0;
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");
        using var writer = new StringWriter();

        var agent = new ChatAgent(
            (_, _, _) => throw new NotSupportedException(),
            (_, _, _) =>
            {
                streamInvocationCount++;
                return streamInvocationCount switch
                {
                    1 => ToAsyncEnumerable(
                    [
                        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                            toolCallUpdates:
                            [
                                OpenAIChatModelFactory.StreamingChatToolCallUpdate(
                                    index: 0,
                                    toolCallId: "call_1",
                                    functionName: FetchUrlTool.ToolName,
                                    functionArgumentsUpdate: BinaryData.FromString("""{"url":"https://example.com"}""")),
                            ]),
                        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                            finishReason: ChatFinishReason.ToolCalls),
                    ]),
                    2 => ToAsyncEnumerable(
                    [
                        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                            contentUpdate: [ChatMessageContentPart.CreateTextPart("Streamed ")]),
                        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                            contentUpdate: [ChatMessageContentPart.CreateTextPart("summary")]),
                        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                            finishReason: ChatFinishReason.Stop),
                    ]),
                    _ => throw new InvalidOperationException("Unexpected extra streaming request."),
                };
            },
            toolRegistry,
            writer);

        await agent.RunStreamingAsync(
            new AskRequest("Summarize https://example.com", "gpt-5-mini", null, true),
            CancellationToken.None);

        Assert.Equal($"Streamed summary{Environment.NewLine}", writer.ToString());
        Assert.Equal(2, streamInvocationCount);
        Assert.Equal(1, toolRegistry.ExecutionCount);
    }

    private static IAsyncEnumerable<StreamingChatCompletionUpdate> ToAsyncEnumerable(
        IEnumerable<StreamingChatCompletionUpdate> updates)
        => AsyncEnumerableHelpers.ToAsyncEnumerable(updates);
}
