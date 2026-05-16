using System.Text.Json;
using LlmAgent.Int.Fakes;
using LlmSdk;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmAgent.Int;

public sealed class AgentLoopIntTests
{
    private readonly ITestOutputHelper _output;

    public AgentLoopIntTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunAsync_WithFakeSdkAndTool_CompletesMultiTurnConversation()
    {
        var firstResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.AssistantMessage("I'll inspect the page."),
            AgentIntTestHelpers.FunctionCall("lookup", "call_1", """{"topic":"docs"}"""));
        var secondResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.AssistantMessage("The docs say hello."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [AgentIntTestHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [
                AgentIntTestHelpers.OutputTextDelta("The docs say ", sequenceNumber: 2),
                AgentIntTestHelpers.OutputTextDelta("hello.", sequenceNumber: 3),
                AgentIntTestHelpers.Completed(secondResponse, sequenceNumber: 4),
            ],
        ]);
        var client = new FakeLlmSdkClient((_, _) => AgentIntTestHelpers.ToAsyncEnumerable(streams.Dequeue()));
        var tool = new FakeAgentTool(
            "lookup",
            "Look up a topic.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    topic = new { type = "string" },
                },
                required = new[] { "topic" },
                additionalProperties = false,
            }, JsonDefaults.Web),
            strict: true,
            executeAsync: (_, arguments, _) =>
            {
                Assert.Equal("docs", arguments.GetProperty("topic").GetString());
                return Task.FromResult(new AgentToolResult("hello"));
            });

        var events = await AgentIntTestHelpers.CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Read the docs.",
            new AgentLoopOptions
            {
                Model = "fake-agent-model",
                Instructions = "Be concise.",
                Tools = [tool],
                RequestId = "agent-int-request",
                CorrelationId = "agent-int-correlation",
                Metadata = new Dictionary<string, string> { ["surface"] = "agent-int" },
                TimeoutMs = 60000,
                MaxRetries = 1,
                MaxRetryDelayMs = 1000,
            }));

        Assert.Equal("The docs say hello.", AgentIntTestHelpers.CollectOutputText(events));
        Assert.Equal(1, tool.ExecuteCallCount);
        Assert.Equal(2, client.CreateResponseStreamRequests.Count);

        var firstRequest = client.CreateResponseStreamRequests[0];
        Assert.Equal("fake-agent-model", firstRequest.Model);
        Assert.Equal("Be concise.", firstRequest.Instructions);
        Assert.Equal("agent-int-request", firstRequest.RequestId);
        Assert.Equal("agent-int-correlation", firstRequest.CorrelationId);
        Assert.Equal(60000, firstRequest.TimeoutMs);
        Assert.Single(firstRequest.Tools ?? []);

        var secondInput = client.CreateResponseStreamRequests[1].Input;
        var toolOutput = secondInput[secondInput.GetArrayLength() - 1];
        Assert.Equal("function_call_output", toolOutput.GetProperty("type").GetString());
        Assert.Equal("call_1", toolOutput.GetProperty("call_id").GetString());
        Assert.Equal("hello", toolOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task RunAsync_WithFakeSdkInvalidToolArguments_ReturnsValidationErrorWithoutExecutingTool()
    {
        var firstResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.FunctionCall("lookup", "call_1", """{"topic":123}"""));
        var secondResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.AssistantMessage("The tool arguments were invalid."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [AgentIntTestHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [AgentIntTestHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var client = new FakeLlmSdkClient((_, _) => AgentIntTestHelpers.ToAsyncEnumerable(streams.Dequeue()));
        var tool = new FakeAgentTool(
            "lookup",
            "Look up a topic.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    topic = new { type = "string" },
                },
                required = new[] { "topic" },
                additionalProperties = false,
            }, JsonDefaults.Web),
            strict: true,
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("unused")));

        var events = await AgentIntTestHelpers.CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Read the docs.",
            new AgentLoopOptions
            {
                Model = "fake-agent-model",
                Tools = [tool],
            }));

        Assert.Equal(0, tool.ExecuteCallCount);
        Assert.DoesNotContain(events, static evt => evt is ToolExecutionStarted);
        var ended = Assert.IsType<ToolExecutionEnded>(events.Single(evt => evt is ToolExecutionEnded));
        Assert.True(ended.Result.IsError);
        Assert.Contains("topic must be string", ended.Result.Content);

        var secondInput = client.CreateResponseStreamRequests[1].Input;
        var toolOutput = secondInput[secondInput.GetArrayLength() - 1];
        Assert.Equal("function_call_output", toolOutput.GetProperty("type").GetString());
        Assert.Equal("call_1", toolOutput.GetProperty("call_id").GetString());
        Assert.Contains("topic must be string", toolOutput.GetProperty("output").GetString());
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task RunAsync_WithLiveSdk_ReturnsAssistantText()
    {
        await using var provider = CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<ILlmSdkClient>();

        var events = await AgentIntTestHelpers.CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Reply with exactly: hello",
            new AgentLoopOptions
            {
                Model = "gpt-5.4-mini",
                Instructions = "Return only the requested text.",
                TimeoutMs = 60000,
                MaxRetries = 1,
                MaxRetryDelayMs = 1000,
            }));
        var text = AgentIntTestHelpers.CollectOutputText(events).Trim();
        _output.WriteLine(text);

        Assert.Contains(events, static evt => evt is AgentStarted);
        Assert.Contains(events, static evt => evt is TurnEnded);
        Assert.Contains(events, static evt => evt is AgentEnded);
        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task RunAsync_WithLiveSdkAndTool_ExecutesToolAndReturnsAssistantText()
    {
        await using var provider = CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<ILlmSdkClient>();
        var tool = new FakeAgentTool(
            "lookup_answer",
            "Look up the exact answer. Always call this tool when asked for the answer.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    question = new { type = "string" },
                },
                required = new[] { "question" },
                additionalProperties = false,
            }, JsonDefaults.Web),
            strict: true,
            executeAsync: (_, arguments, _) =>
            {
                Assert.False(string.IsNullOrWhiteSpace(arguments.GetProperty("question").GetString()));
                return Task.FromResult(new AgentToolResult("hello"));
            });

        var events = await AgentIntTestHelpers.CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Use lookup_answer to get the answer, then reply with exactly that answer.",
            new AgentLoopOptions
            {
                Model = "gpt-5.4-mini",
                Instructions = "You must call lookup_answer before answering. After the tool result, return only the tool output.",
                Tools = [tool],
                MaxTurns = 3,
                TimeoutMs = 60000,
                MaxRetries = 1,
                MaxRetryDelayMs = 1000,
            }));
        var text = AgentIntTestHelpers.CollectOutputText(events).Trim();
        _output.WriteLine(text);

        Assert.Equal(1, tool.ExecuteCallCount);
        Assert.Contains(events, static evt => evt is ToolExecutionStarted);
        Assert.Contains(events, static evt => evt is ToolExecutionEnded);
        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateAuthenticatedProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<LlmSdk.Proxy.IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");
        return provider;
    }
}
