using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class SdkContextAskAgentTests
{
    [Fact]
    public async Task RunAsync_WritesTextAndBuildsPortableContext()
    {
        using var writer = new StringWriter();
        var client = FakeLlmSdkClient.WithContextResponses(new AssistantMessage(
            [new TextContent("Hello from context")],
            StopReason.Stop));

        await SdkContextAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest(
                "Hi there",
                "gpt-5.4-mini",
                "Be brief",
                false,
                RequestId: "request-123",
                CorrelationId: "correlation-123",
                Metadata: new Dictionary<string, string> { ["trace"] = "trace-123" },
                TimeoutMs: 10000,
                MaxRetries: 1,
                MaxRetryDelayMs: 500),
            CompletionApi.Responses,
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello from context{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCompleteContext);
        Assert.Equal("Be brief", client.LastCompleteContext!.System);
        var userMessage = Assert.IsType<UserMessage>(Assert.Single(client.LastCompleteContext.Messages));
        Assert.Equal("Hi there", Assert.IsType<TextContent>(Assert.Single(userMessage.Content)).Text);
        Assert.Equal("gpt-5.4-mini", client.LastCompletionOptions?.Model);
        Assert.Equal(CompletionApi.Responses, client.LastCompletionOptions?.PreferredApi);
        Assert.Equal("request-123", client.LastCompletionOptions?.RequestId);
        Assert.Equal("correlation-123", client.LastCompletionOptions?.CorrelationId);
        Assert.Equal("trace-123", client.LastCompletionOptions?.Metadata?["trace"]);
        Assert.Equal(10000, client.LastCompletionOptions?.TimeoutMs);
        Assert.Equal(1, client.LastCompletionOptions?.MaxRetries);
        Assert.Equal(500, client.LastCompletionOptions?.MaxRetryDelayMs);
    }

    [Fact]
    public async Task RunAsync_WhenStopReasonIsNotStop_WritesWarning()
    {
        using var writer = new StringWriter();
        var client = FakeLlmSdkClient.WithContextResponses(new AssistantMessage(
            [new TextContent("Use a tool")],
            StopReason.ToolUse));

        await SdkContextAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "claude-haiku-4.5", null, false),
            CompletionApi.ChatCompletions,
            writer,
            CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Use a tool", output);
        Assert.Contains("Stop reason: ToolUse", output);
        Assert.Equal(CompletionApi.ChatCompletions, client.LastCompletionOptions?.PreferredApi);
    }

    [Fact]
    public async Task RunStreamingAsync_WritesTextDeltasAndBuildsPortableContext()
    {
        using var writer = new StringWriter();
        var client = FakeLlmSdkClient.WithContextStreams(
        [
            new TextDelta("Hello "),
            new TextDelta("from stream"),
            new StreamDone(new AssistantMessage([new TextContent("Hello from stream")], StopReason.Stop)),
        ]);

        await SdkContextAskAgent.RunStreamingAsync(
            client,
            new AskRequest("Stream this", "gpt-5.4-mini", "Be brief", true),
            CompletionApi.Responses,
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello from stream{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastStreamContext);
        Assert.Equal("Be brief", client.LastStreamContext!.System);
        var userMessage = Assert.IsType<UserMessage>(Assert.Single(client.LastStreamContext.Messages));
        Assert.Equal("Stream this", Assert.IsType<TextContent>(Assert.Single(userMessage.Content)).Text);
        Assert.Equal("gpt-5.4-mini", client.LastStreamOptions?.Model);
        Assert.Equal(CompletionApi.Responses, client.LastStreamOptions?.PreferredApi);
    }

    [Fact]
    public async Task RunStreamingAsync_WhenStreamErrors_WritesError()
    {
        using var writer = new StringWriter();
        var client = FakeLlmSdkClient.WithContextStreams(
        [
            new StreamError(new AssistantMessage([], StopReason.Error), "upstream timeout"),
        ]);

        await SdkContextAskAgent.RunStreamingAsync(
            client,
            new AskRequest("Stream this", "claude-haiku-4.5", null, true),
            CompletionApi.ChatCompletions,
            writer,
            CancellationToken.None);

        Assert.Equal($"Stream error: upstream timeout{Environment.NewLine}", writer.ToString());
        Assert.Equal(CompletionApi.ChatCompletions, client.LastStreamOptions?.PreferredApi);
    }
}
