using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task RunAsync_WithFakeSdk_ForwardsSdkRequestOptionsAcrossTurns()
    {
        var firstResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.FunctionCall("lookup", "call_1", """{"topic":"docs"}"""));
        var secondResponse = AgentIntTestHelpers.CreateResponse(
            AgentIntTestHelpers.AssistantMessage("The docs say hello."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [AgentIntTestHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [AgentIntTestHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var client = new FakeLlmSdkClient((_, _) => AgentIntTestHelpers.ToAsyncEnumerable(streams.Dequeue()));
        var headers = new Dictionary<string, string> { ["X-Agent-Test"] = "forwarding" };
        var metadata = new Dictionary<string, string> { ["surface"] = "agent-int" };
        Func<JsonNode, JsonNode?> onPayload = static payload => payload;
        Action<ResponseSnapshot> onResponse = static _ => { };
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
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("hello")));

        var events = await AgentIntTestHelpers.CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Read the docs.",
            new AgentLoopOptions
            {
                Model = "fake-agent-model",
                Instructions = "Be concise.",
                Tools = [tool],
                Headers = headers,
                PromptCacheKey = "agent-int-session",
                RequestId = "agent-int-request",
                CorrelationId = "agent-int-correlation",
                Metadata = metadata,
                TimeoutMs = 60000,
                MaxRetries = 1,
                MaxRetryDelayMs = 1000,
                OnPayload = onPayload,
                OnResponse = onResponse,
            }));

        Assert.Contains(events, static evt => evt is AgentEnded);
        Assert.Equal(2, client.CreateResponseStreamRequests.Count);
        foreach (var request in client.CreateResponseStreamRequests)
        {
            Assert.Same(headers, request.Headers);
            Assert.Equal("agent-int-session", request.PromptCacheKey);
            Assert.Equal("agent-int-request", request.RequestId);
            Assert.Equal("agent-int-correlation", request.CorrelationId);
            Assert.Same(metadata, request.Metadata);
            Assert.Equal(60000, request.TimeoutMs);
            Assert.Equal(1, request.MaxRetries);
            Assert.Equal(1000, request.MaxRetryDelayMs);
            Assert.Same(onPayload, request.OnPayload);
            Assert.Same(onResponse, request.OnResponse);
        }
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
                PromptCacheKey = $"agent-live-smoke-{Guid.NewGuid():N}",
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
