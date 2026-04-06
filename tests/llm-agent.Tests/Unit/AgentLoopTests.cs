using System.Text.Json;
using LlmAgent.Tests.Fakes;
using LlmAgent.Tests.Helpers;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Unit;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_WithNoToolCalls_EmitsExpectedSingleTurnLifecycle()
    {
        var response = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.OutputTextDelta("Done.", sequenceNumber: 1),
                StreamHelpers.Completed(response, sequenceNumber: 2)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        Assert.Collection(
            events,
            agentStarted => Assert.IsType<AgentStarted>(agentStarted),
            turnStarted => Assert.IsType<TurnStarted>(turnStarted),
            messageStarted => Assert.IsType<MessageStarted>(messageStarted),
            messageDelta =>
            {
                var delta = Assert.IsType<MessageDelta>(messageDelta);
                var streamEvent = Assert.IsType<OutputTextDeltaEvent>(delta.StreamEvent);
                Assert.Equal("Done.", streamEvent.Delta);
            },
            messageEnded =>
            {
                var ended = Assert.IsType<MessageEnded>(messageEnded);
                Assert.Same(response, ended.Response);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Same(response, ended.Response);
                Assert.Empty(ended.ToolResults);
            },
            agentEnded =>
            {
                var ended = Assert.IsType<AgentEnded>(agentEnded);
                Assert.Collection(
                    ended.Context.Items,
                    item => Assert.IsType<UserMessageContextItem>(item),
                    item =>
                    {
                        var output = Assert.IsType<ResponseOutputContextItem>(item);
                        Assert.Same(response.Output[0], output.Item);
                    });
            });
    }

    [Fact]
    public async Task RunAsync_MessageDeltaContainsOriginalStreamEvent()
    {
        var response = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streamEvent = StreamHelpers.OutputTextDelta("chunk", sequenceNumber: 1);
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                streamEvent,
                StreamHelpers.Completed(response, sequenceNumber: 2)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        var messageDelta = Assert.IsType<MessageDelta>(events.Single(evt => evt is MessageDelta));
        Assert.Same(streamEvent, messageDelta.StreamEvent);
    }

    [Fact]
    public async Task RunAsync_MessageEndedContainsCompletedResponse()
    {
        var response = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(response, sequenceNumber: 1)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        var messageEnded = Assert.IsType<MessageEnded>(events.Single(evt => evt is MessageEnded));
        Assert.Same(response, messageEnded.Response);
    }

    [Fact]
    public async Task RunAsync_AgentEndedCarriesTypedContext()
    {
        var response = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(response, sequenceNumber: 1)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        var agentEnded = Assert.IsType<AgentEnded>(events.Single(evt => evt is AgentEnded));
        Assert.Collection(
            agentEnded.Context.Items,
            item =>
            {
                var user = Assert.IsType<UserMessageContextItem>(item);
                Assert.Equal("Hello", user.Text);
            },
            item =>
            {
                var output = Assert.IsType<ResponseOutputContextItem>(item);
                Assert.IsType<ResponseMessageItem>(output.Item);
            });
    }

    [Fact]
    public async Task RunAsync_ResponseWithFunctionCall_ExecutesToolAndEmitsToolEvents()
    {
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.AssistantMessage("I'll read it."),
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("file contents")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(streams.Dequeue()));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.Equal(1, tool.ExecuteCallCount);
        Assert.Collection(
            events.Where(static evt => evt is ToolExecutionStarted or ToolExecutionEnded),
            toolStarted =>
            {
                var started = Assert.IsType<ToolExecutionStarted>(toolStarted);
                Assert.Equal("call_1", started.CallId);
                Assert.Equal("read_file", started.ToolName);
                Assert.Equal("{\"path\":\"test.txt\"}", started.Arguments);
            },
            toolEnded =>
            {
                var ended = Assert.IsType<ToolExecutionEnded>(toolEnded);
                Assert.Equal("call_1", ended.CallId);
                Assert.Equal("read_file", ended.ToolName);
                Assert.Equal("file contents", ended.Result.Content);
                Assert.False(ended.Result.IsError);
            });
    }

    [Fact]
    public async Task RunAsync_ParsesFunctionCallArgumentsBeforeExecutingTool()
    {
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, arguments, _) =>
            {
                Assert.Equal("test.txt", arguments.GetProperty("path").GetString());
                return Task.FromResult(new AgentToolResult("file contents"));
            });
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(streams.Dequeue()));

        await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.True(tool.LastArguments.HasValue);
        Assert.Equal("test.txt", tool.LastArguments.Value.GetProperty("path").GetString());
    }

    [Fact]
    public async Task RunAsync_FeedsToolResultBackInNextTurnInput()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("file contents")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.Equal(2, requests.Count);
        var secondInput = requests[1].Input;
        var toolOutput = secondInput[secondInput.GetArrayLength() - 1];
        Assert.Equal("function_call_output", toolOutput.GetProperty("type").GetString());
        Assert.Equal("call_1", toolOutput.GetProperty("call_id").GetString());
        Assert.Equal("file contents", toolOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task RunAsync_AppendsResponseOutputToNextTurnContext()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.AssistantMessage("I'll read it.", id: "msg_1"),
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}", id: "fc_1"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("file contents")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.Equal(2, requests.Count);
        var secondInput = requests[1].Input;
        Assert.Equal(4, secondInput.GetArrayLength());
        Assert.Equal("message", secondInput[0].GetProperty("type").GetString());
        Assert.Equal("message", secondInput[1].GetProperty("type").GetString());
        Assert.Equal("function_call", secondInput[2].GetProperty("type").GetString());
        Assert.Equal("read_file", secondInput[2].GetProperty("name").GetString());
        Assert.Equal("function_call_output", secondInput[3].GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ExecutesMultipleToolCallsSequentially()
    {
        var executionOrder = new List<string>();
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}", id: "fc_1"),
            StreamHelpers.FunctionCall("summarize", "call_2", "{\"text\":\"hello\"}", id: "fc_2"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tools = new IAgentTool[]
        {
            new FakeAgentTool(
                "read_file",
                "Read a file.",
                executeAsync: (_, _, _) =>
                {
                    executionOrder.Add("read_file");
                    return Task.FromResult(new AgentToolResult("file contents"));
                }),
            new FakeAgentTool(
                "summarize",
                "Summarize text.",
                executeAsync: (_, _, _) =>
                {
                    executionOrder.Add("summarize");
                    return Task.FromResult(new AgentToolResult("summary"));
                }),
        };
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        await CollectEventsAsync(AgentLoop.RunAsync(client, "Do both", CreateOptions(tools)));

        Assert.Equal(["read_file", "summarize"], executionOrder);
        var secondInput = requests[1].Input;
        Assert.Equal("call_1", secondInput[3].GetProperty("call_id").GetString());
        Assert.Equal("call_2", secondInput[4].GetProperty("call_id").GetString());
    }

    [Fact]
    public async Task RunAsync_ToolExceptionReturnsErrorResultToModel()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => throw new InvalidOperationException("boom"));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        var toolEnded = Assert.IsType<ToolExecutionEnded>(events.Single(evt => evt is ToolExecutionEnded));
        Assert.Equal("boom", toolEnded.Result.Content);
        Assert.True(toolEnded.Result.IsError);

        var toolOutput = requests[1].Input[requests[1].Input.GetArrayLength() - 1];
        Assert.Equal("boom", toolOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task RunAsync_MissingToolReturnsErrorResultToModel()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("missing_tool", "call_1", "{\"path\":\"test.txt\"}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions()));

        var toolEnded = Assert.IsType<ToolExecutionEnded>(events.Single(evt => evt is ToolExecutionEnded));
        Assert.Equal("Tool 'missing_tool' not found.", toolEnded.Result.Content);
        Assert.True(toolEnded.Result.IsError);

        var toolOutput = requests[1].Input[requests[1].Input.GetArrayLength() - 1];
        Assert.Equal("Tool 'missing_tool' not found.", toolOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task RunAsync_InvalidToolArgumentsReturnsErrorResultToModel()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{invalid"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("unused")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        var toolEnded = Assert.IsType<ToolExecutionEnded>(events.Single(evt => evt is ToolExecutionEnded));
        Assert.StartsWith("Invalid arguments:", toolEnded.Result.Content);
        Assert.True(toolEnded.Result.IsError);
        Assert.Equal(0, tool.ExecuteCallCount);
    }

    [Fact]
    public async Task RunAsync_ResponseFailedTerminatesLoop()
    {
        var failedResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Failed."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Failed(failedResponse, sequenceNumber: 1)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        Assert.Collection(
            events,
            agentStarted => Assert.IsType<AgentStarted>(agentStarted),
            turnStarted => Assert.IsType<TurnStarted>(turnStarted),
            messageStarted => Assert.IsType<MessageStarted>(messageStarted),
            messageEnded =>
            {
                var ended = Assert.IsType<MessageEnded>(messageEnded);
                Assert.Same(failedResponse, ended.Response);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Same(failedResponse, ended.Response);
                Assert.Empty(ended.ToolResults);
            },
            agentEnded => Assert.IsType<AgentEnded>(agentEnded));
    }

    [Fact]
    public async Task RunAsync_ResponseIncompleteTerminatesLoop()
    {
        var incompleteResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Incomplete."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Incomplete(incompleteResponse, sequenceNumber: 1)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Hello", CreateOptions()));

        Assert.Collection(
            events,
            agentStarted => Assert.IsType<AgentStarted>(agentStarted),
            turnStarted => Assert.IsType<TurnStarted>(turnStarted),
            messageStarted => Assert.IsType<MessageStarted>(messageStarted),
            messageEnded =>
            {
                var ended = Assert.IsType<MessageEnded>(messageEnded);
                Assert.Same(incompleteResponse, ended.Response);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Same(incompleteResponse, ended.Response);
                Assert.Empty(ended.ToolResults);
            },
            agentEnded => Assert.IsType<AgentEnded>(agentEnded));
    }

    [Fact]
    public async Task RunAsync_ContinuesUntilResponseHasNoToolCalls()
    {
        var requests = new List<CreateResponseRequest>();
        var response1 = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("first_tool", "call_1", "{\"value\":1}", id: "fc_1"));
        var response2 = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("second_tool", "call_2", "{\"value\":2}", id: "fc_2"));
        var response3 = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(response1, sequenceNumber: 1)],
            [StreamHelpers.Completed(response2, sequenceNumber: 2)],
            [StreamHelpers.Completed(response3, sequenceNumber: 3)],
        ]);
        var executionOrder = new List<string>();
        var tools = new IAgentTool[]
        {
            new FakeAgentTool(
                "first_tool",
                "First tool.",
                executeAsync: (_, _, _) =>
                {
                    executionOrder.Add("first_tool");
                    return Task.FromResult(new AgentToolResult("one"));
                }),
            new FakeAgentTool(
                "second_tool",
                "Second tool.",
                executeAsync: (_, _, _) =>
                {
                    executionOrder.Add("second_tool");
                    return Task.FromResult(new AgentToolResult("two"));
                }),
        };
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Keep going", CreateOptions(tools)));

        Assert.Equal(3, requests.Count);
        Assert.Equal(["first_tool", "second_tool"], executionOrder);
        Assert.Equal(3, events.Count(evt => evt is TurnStarted));
        Assert.Equal(2, events.Count(evt => evt is ToolExecutionEnded));
    }

    [Fact]
    public async Task RunAsync_MaxTurnsStopsAfterConfiguredTurns()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}", id: "fc_1"));
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("file contents")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(StreamHelpers.Completed(firstResponse, sequenceNumber: 1));
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Read test.txt",
            CreateOptions([tool], maxTurns: 1)));

        Assert.Single(requests);
        Assert.Equal(1, tool.ExecuteCallCount);
        Assert.IsType<AgentEnded>(events[^1]);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsLoop()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done.")), sequenceNumber: 1)));

        await using var enumerator = AgentLoop.RunAsync(
            client,
            "Hello",
            CreateOptions(),
            cancellationTokenSource.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.IsType<AgentStarted>(enumerator.Current);

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });
    }

    [Fact]
    public async Task RunAsync_PassesOptionsThroughToCreateResponseRequest()
    {
        var parameters = JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                    },
                },
            },
            JsonDefaults.Web);
        var tool = new FakeAgentTool("read_file", "Read a file.", parameters, strict: true);
        var reasoning = new ResponseReasoning
        {
            Effort = "high",
            Summary = "auto",
        };
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done.")), sequenceNumber: 1)));

        await CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Hello",
            CreateOptions(
                [tool],
                model: "gpt-4.1",
                instructions: "You are helpful.",
                temperature: 0.25,
                reasoning: reasoning)));

        var request = client.LastCreateResponseStreamRequest;
        Assert.NotNull(request);
        Assert.Equal("gpt-4.1", request.Model);
        Assert.Equal("You are helpful.", request.Instructions);
        Assert.Equal(0.25, request.Temperature);
        Assert.Same(reasoning, request.Reasoning);
        Assert.True(request.Stream);
        Assert.NotNull(request.Tools);
        Assert.Single(request.Tools);
        Assert.Equal("read_file", request.Tools[0].Name);
        Assert.Equal("Read a file.", request.Tools[0].Description);
        Assert.True(request.Tools[0].Strict);
        Assert.Equal("message", request.Input[0].GetProperty("type").GetString());
        Assert.Equal("Hello", request.Input[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    private static AgentLoopOptions CreateOptions(
        IReadOnlyList<IAgentTool>? tools = null,
        int? maxTurns = null,
        string model = "gpt-4.1",
        string? instructions = null,
        double? temperature = null,
        ResponseReasoning? reasoning = null) => new()
        {
            Model = model,
            Instructions = instructions,
            Tools = tools ?? [],
            MaxTurns = maxTurns,
            Temperature = temperature,
            Reasoning = reasoning,
        };

    private static async Task<List<AgentEvent>> CollectEventsAsync(IAsyncEnumerable<AgentEvent> events)
    {
        var results = new List<AgentEvent>();

        await foreach (var agentEvent in events)
        {
            results.Add(agentEvent);
        }

        return results;
    }
}
