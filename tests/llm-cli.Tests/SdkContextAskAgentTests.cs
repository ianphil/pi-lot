using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using LlmSdk.Core.Models;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class SdkContextAskAgentTests
{
    [Fact]
    public async Task RunAsync_WritesTextAndBuildsPortableContext()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            completeAsync: (_, _, _) => Task.FromResult(new AssistantMessage(
                [new TextContent("Hello from context")],
                StopReason.Stop)));

        await SdkContextAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", "Be brief", false),
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
    }

    [Fact]
    public async Task RunAsync_WhenStopReasonIsNotStop_WritesWarning()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            completeAsync: (_, _, _) => Task.FromResult(new AssistantMessage(
                [new TextContent("Use a tool")],
                StopReason.ToolUse)));

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
}
