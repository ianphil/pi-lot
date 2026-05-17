using System.Text.Json;
using System.Text.Json.Nodes;
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
                var streamEvent = Assert.IsType<TextDelta>(delta.StreamEvent);
                Assert.Equal("Done.", streamEvent.Text);
            },
            messageEnded =>
            {
                var ended = Assert.IsType<MessageEnded>(messageEnded);
                Assert.Equal("Done.", Assert.IsType<TextContent>(ended.Message.Content.Single()).Text);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Equal(StopReason.Stop, ended.Message.StopReason);
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
                        var output = Assert.IsType<AssistantResponseContextItem>(item);
                        Assert.Equal("Done.", Assert.IsType<TextContent>(output.Message.Content.Single()).Text);
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
        var textDelta = Assert.IsType<TextDelta>(messageDelta.StreamEvent);
        Assert.Equal(streamEvent.Delta, textDelta.Text);
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
        Assert.Equal("Done.", Assert.IsType<TextContent>(messageEnded.Message.Content.Single()).Text);
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
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.IsType<TextContent>(output.Message.Content.Single());
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
            CreateObjectSchema(
                new
                {
                    path = new { type = "string" },
                },
                ["path"]),
            strict: true,
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
    public async Task RunAsync_InvalidSchemaToolArgumentsReturnsErrorResultWithoutExecutingTool()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":42,\"extra\":true}"));
        var secondResponse = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            CreateObjectSchema(
                new
                {
                    path = new { type = "string" },
                },
                ["path"],
                additionalProperties: false),
            strict: true,
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("unused")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.Equal(0, tool.ExecuteCallCount);
        Assert.DoesNotContain(events, static evt => evt is ToolExecutionStarted or ToolExecutionEnded);
        Assert.Single(requests);
        var messageEnded = Assert.IsType<MessageEnded>(events.Single(static evt => evt is MessageEnded));
        var toolOutput = Assert.IsType<ToolResultContent>(messageEnded.Message.Content.Single());
        Assert.Equal("call_1", toolOutput.ToolCallId);
        Assert.True(toolOutput.IsError);
        Assert.Contains("Tool argument validation failed", toolOutput.Output, StringComparison.Ordinal);
        Assert.Contains("path must be string", toolOutput.Output, StringComparison.Ordinal);
        Assert.Contains("extra is not allowed", toolOutput.Output, StringComparison.Ordinal);
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
        Assert.Equal("user", secondInput[0].GetProperty("role").GetString());
        Assert.Equal("assistant", secondInput[1].GetProperty("role").GetString());
        Assert.Equal("function_call", secondInput[2].GetProperty("type").GetString());
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
        Assert.Equal("call_1", secondInput[1].GetProperty("call_id").GetString());
        Assert.Equal("call_2", secondInput[2].GetProperty("call_id").GetString());
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

        Assert.DoesNotContain(events, static evt => evt is ToolExecutionStarted or ToolExecutionEnded);
        var messageEnded = Assert.IsType<MessageEnded>(events.Single(static evt => evt is MessageEnded));
        var toolOutput = Assert.IsType<ToolResultContent>(messageEnded.Message.Content.Single());
        Assert.Equal("call_1", toolOutput.ToolCallId);
        Assert.True(toolOutput.IsError);
        Assert.Contains("arguments must be valid JSON", toolOutput.Output, StringComparison.Ordinal);
        Assert.Equal(0, tool.ExecuteCallCount);
    }

    [Fact]
    public async Task RunAsync_FunctionCallArgumentDeltasRemainObservableThroughMessageDelta()
    {
        var response = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}"));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.FunctionCallArgumentsDelta("{\"path\":\"", sequenceNumber: 1),
                StreamHelpers.FunctionCallArgumentsDelta("test.txt\"}", sequenceNumber: 2),
                StreamHelpers.Completed(response, sequenceNumber: 3)));

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions(maxTurns: 1)));

        var deltas = events.OfType<MessageDelta>()
            .Select(static evt => evt.StreamEvent)
            .OfType<ToolCallDelta>()
            .ToArray();
        Assert.Collection(
            deltas,
            delta => Assert.Equal("{\"path\":\"", delta.ArgumentsJsonChunk),
            delta => Assert.Equal("test.txt\"}", delta.ArgumentsJsonChunk));
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
                Assert.Equal(StopReason.Error, ended.Message.StopReason);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Equal(StopReason.Error, ended.Message.StopReason);
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
                Assert.Equal("Incomplete.", Assert.IsType<TextContent>(ended.Message.Content.Single()).Text);
            },
            turnEnded =>
            {
                var ended = Assert.IsType<TurnEnded>(turnEnded);
                Assert.Equal(StopReason.Stop, ended.Message.StopReason);
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
                StreamHelpers.Completed(StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done.")), sequenceNumber: 1)))
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-4.1",
                    Capabilities = new ModelCapabilities
                    {
                        Supports = new ModelSupports { ReasoningEffort = ["high"] },
                    },
                },
            ],
        };

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
        Assert.Null(request.Reasoning);
        Assert.Null(request.Stream);
        Assert.NotNull(request.Tools);
        Assert.Single(request.Tools);
        Assert.Equal("read_file", request.Tools[0].Name);
        Assert.Equal("Read a file.", request.Tools[0].Description);
        Assert.True(request.Tools[0].Strict);
        Assert.Equal("user", request.Input[0].GetProperty("role").GetString());
        Assert.Equal("Hello", request.Input[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task RunAsync_ForwardsSdkRequestOptionsToResponseStream()
    {
        var headers = new Dictionary<string, string> { ["X-Debug"] = "enabled" };
        var metadata = new Dictionary<string, string> { ["surface"] = "agent-unit" };
        Func<JsonNode, JsonNode?> onPayload = static payload => payload;
        Action<ResponseSnapshot> onResponse = static _ => { };
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done.")), sequenceNumber: 1)));

        await CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Hello",
            CreateOptions(
                headers: headers,
                promptCacheKey: "agent-session",
                requestId: "agent-request",
                correlationId: "agent-correlation",
                metadata: metadata,
                timeoutMs: 60000,
                maxRetries: 2,
                maxRetryDelayMs: 1000,
                onPayload: onPayload,
                onResponse: onResponse)));

        var request = client.LastCreateResponseStreamRequest;
        Assert.NotNull(request);
        Assert.Same(headers, request.Headers);
        Assert.Equal("agent-session", request.PromptCacheKey);
        Assert.Equal("agent-request", request.RequestId);
        Assert.Equal("agent-correlation", request.CorrelationId);
        Assert.Same(metadata, request.Metadata);
        Assert.Equal(60000, request.TimeoutMs);
        Assert.Equal(2, request.MaxRetries);
        Assert.Equal(1000, request.MaxRetryDelayMs);
        Assert.Same(onPayload, request.OnPayload);
        Assert.Same(onResponse, request.OnResponse);
    }

    [Fact]
    public async Task RunAsync_ForwardsSdkRequestOptionsOnEveryTurn()
    {
        var requests = new List<CreateResponseRequest>();
        var headers = new Dictionary<string, string> { ["X-Agent"] = "test" };
        var metadata = new Dictionary<string, string> { ["surface"] = "agent-unit" };
        Func<JsonNode, JsonNode?> onPayload = static _ => null;
        Action<ResponseSnapshot> onResponse = static _ => { };
        var firstResponse = StreamHelpers.CreateResponse(
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

        await CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Read test.txt",
            CreateOptions(
                [tool],
                headers: headers,
                promptCacheKey: "agent-session",
                requestId: "agent-request",
                correlationId: "agent-correlation",
                metadata: metadata,
                timeoutMs: 60000,
                maxRetries: 2,
                maxRetryDelayMs: 1000,
                onPayload: onPayload,
                onResponse: onResponse)));

        Assert.Equal(2, requests.Count);
        foreach (var request in requests)
        {
            Assert.Same(headers, request.Headers);
            Assert.Equal("agent-session", request.PromptCacheKey);
            Assert.Equal("agent-request", request.RequestId);
            Assert.Equal("agent-correlation", request.CorrelationId);
            Assert.Same(metadata, request.Metadata);
            Assert.Equal(60000, request.TimeoutMs);
            Assert.Equal(2, request.MaxRetries);
            Assert.Equal(1000, request.MaxRetryDelayMs);
            Assert.Same(onPayload, request.OnPayload);
            Assert.Same(onResponse, request.OnResponse);
        }
    }

    [Fact]
    public async Task RunAsync_FullMultiTurnScenario_EmitsCompleteEventSequenceAndTypedContext()
    {
        var requests = new List<CreateResponseRequest>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.AssistantMessage("I'll read it.", id: "msg_1"),
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}", id: "fc_1"));
        var secondResponse = StreamHelpers.CreateResponse(
            StreamHelpers.AssistantMessage("The file says hello.", id: "msg_2"));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [
                StreamHelpers.OutputTextDelta("I'll read it.", sequenceNumber: 1, itemId: "msg_1"),
                StreamHelpers.Completed(firstResponse, sequenceNumber: 2),
            ],
            [
                StreamHelpers.OutputTextDelta("The file says hello.", sequenceNumber: 3, itemId: "msg_2"),
                StreamHelpers.Completed(secondResponse, sequenceNumber: 4),
            ],
        ]);
        var tool = new FakeAgentTool(
            "read_file",
            "Read a file.",
            executeAsync: (_, _, _) => Task.FromResult(new AgentToolResult("hello from file")));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Read test.txt", CreateOptions([tool])));

        Assert.Equal(
            [
                typeof(AgentStarted),
                typeof(TurnStarted),
                typeof(MessageStarted),
                typeof(MessageDelta),
                typeof(MessageEnded),
                typeof(ToolExecutionStarted),
                typeof(ToolExecutionEnded),
                typeof(TurnEnded),
                typeof(TurnStarted),
                typeof(MessageStarted),
                typeof(MessageDelta),
                typeof(MessageEnded),
                typeof(TurnEnded),
                typeof(AgentEnded),
            ],
            events.Select(static evt => evt.GetType()));

        var turnEndedEvents = events.OfType<TurnEnded>().ToArray();
        Assert.Equal(2, turnEndedEvents.Length);
        Assert.Single(turnEndedEvents[0].ToolResults);
        Assert.Equal("call_1", turnEndedEvents[0].ToolResults[0].CallId);
        Assert.Equal("read_file", turnEndedEvents[0].ToolResults[0].ToolName);
        Assert.Equal("hello from file", turnEndedEvents[0].ToolResults[0].Output);
        Assert.Empty(turnEndedEvents[1].ToolResults);

        Assert.Equal(2, requests.Count);
        Assert.Equal(4, requests[1].Input.GetArrayLength());
        Assert.Equal("function_call", requests[1].Input[2].GetProperty("type").GetString());
        Assert.Equal("function_call_output", requests[1].Input[3].GetProperty("type").GetString());
        Assert.Equal("hello from file", requests[1].Input[3].GetProperty("output").GetString());

        var agentEnded = Assert.IsType<AgentEnded>(events[^1]);
        Assert.Collection(
            agentEnded.Context.Items,
            item =>
            {
                var user = Assert.IsType<UserMessageContextItem>(item);
                Assert.Equal("Read test.txt", user.Text);
            },
            item =>
            {
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.Contains(output.Message.Content, static block => block is TextContent { Text: "I'll read it." });
                Assert.Contains(output.Message.Content, static block => block is ToolCallContent { Id: "call_1" });
            },
            item =>
            {
                var toolResult = Assert.IsType<ToolResultContextItem>(item);
                Assert.Equal("call_1", toolResult.CallId);
                Assert.Equal("hello from file", toolResult.Output);
            },
            item =>
            {
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.Contains(output.Message.Content, static block => block is TextContent { Text: "The file says hello." });
            });
    }

    [Fact]
    public async Task RunAsync_TwoConsecutiveToolTurns_PreservesContextAcrossTurns()
    {
        var requests = new List<CreateResponseRequest>();
        var executionOrder = new List<string>();
        var firstResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("read_file", "call_1", "{\"path\":\"test.txt\"}", id: "fc_1"));
        var secondResponse = StreamHelpers.CreateResponse(
            StreamHelpers.FunctionCall("summarize", "call_2", "{\"text\":\"hello from file\"}", id: "fc_2"));
        var thirdResponse = StreamHelpers.CreateResponse(
            StreamHelpers.AssistantMessage("Summary ready.", id: "msg_3"));
        var streams = new Queue<ResponseStreamEvent[]>(
        [
            [StreamHelpers.Completed(firstResponse, sequenceNumber: 1)],
            [StreamHelpers.Completed(secondResponse, sequenceNumber: 2)],
            [StreamHelpers.Completed(thirdResponse, sequenceNumber: 3)],
        ]);
        var tools = new IAgentTool[]
        {
            new FakeAgentTool(
                "read_file",
                "Read a file.",
                executeAsync: (_, _, _) =>
                {
                    executionOrder.Add("read_file");
                    return Task.FromResult(new AgentToolResult("hello from file"));
                }),
            new FakeAgentTool(
                "summarize",
                "Summarize text.",
                executeAsync: (_, arguments, _) =>
                {
                    executionOrder.Add("summarize");
                    Assert.Equal("hello from file", arguments.GetProperty("text").GetString());
                    return Task.FromResult(new AgentToolResult("hello summary"));
                }),
        };
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return StreamHelpers.ToAsyncEnumerable(streams.Dequeue());
            });

        var events = await CollectEventsAsync(AgentLoop.RunAsync(client, "Summarize test.txt", CreateOptions(tools)));

        Assert.Equal(["read_file", "summarize"], executionOrder);
        Assert.Equal(3, requests.Count);
        Assert.Equal(3, events.Count(evt => evt is TurnStarted));
        Assert.Equal(2, events.Count(evt => evt is ToolExecutionEnded));

        var thirdInput = requests[2].Input;
        Assert.Equal(5, thirdInput.GetArrayLength());
        Assert.Equal("function_call", thirdInput[1].GetProperty("type").GetString());
        Assert.Equal("call_1", thirdInput[1].GetProperty("call_id").GetString());
        Assert.Equal("function_call_output", thirdInput[2].GetProperty("type").GetString());
        Assert.Equal("hello from file", thirdInput[2].GetProperty("output").GetString());
        Assert.Equal("function_call", thirdInput[3].GetProperty("type").GetString());
        Assert.Equal("call_2", thirdInput[3].GetProperty("call_id").GetString());
        Assert.Equal("function_call_output", thirdInput[4].GetProperty("type").GetString());
        Assert.Equal("hello summary", thirdInput[4].GetProperty("output").GetString());

        var agentEnded = Assert.IsType<AgentEnded>(events[^1]);
        Assert.Collection(
            agentEnded.Context.Items,
            item =>
            {
                var user = Assert.IsType<UserMessageContextItem>(item);
                Assert.Equal("Summarize test.txt", user.Text);
            },
            item =>
            {
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.Contains(output.Message.Content, static block => block is ToolCallContent { Id: "call_1" });
            },
            item =>
            {
                var toolResult = Assert.IsType<ToolResultContextItem>(item);
                Assert.Equal("call_1", toolResult.CallId);
                Assert.Equal("hello from file", toolResult.Output);
            },
            item =>
            {
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.Contains(output.Message.Content, static block => block is ToolCallContent { Id: "call_2" });
            },
            item =>
            {
                var toolResult = Assert.IsType<ToolResultContextItem>(item);
                Assert.Equal("call_2", toolResult.CallId);
                Assert.Equal("hello summary", toolResult.Output);
            },
            item =>
            {
                var output = Assert.IsType<AssistantResponseContextItem>(item);
                Assert.Contains(output.Message.Content, static block => block is TextContent { Text: "Summary ready." });
            });
    }

    [Fact]
    public async Task RunAsync_WhenContextBudgetExceedsWarningThreshold_EmitsWarningBeforeRequest()
    {
        var response = StreamHelpers.CreateResponse(StreamHelpers.AssistantMessage("Done."));
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => StreamHelpers.ToAsyncEnumerable(
                StreamHelpers.Completed(response, sequenceNumber: 1)))
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4",
                    TokenLimits = new ModelTokenLimits { MaxPromptTokens = 1000000 },
                },
            ],
        };

        var events = await CollectEventsAsync(AgentLoop.RunAsync(
            client,
            "Hello",
            CreateOptions(
                model: "gpt-5.4",
                contextBudget: new AgentContextBudgetOptions
                {
                    WarningThresholdRatio = 0.000001,
                    ErrorThresholdRatio = 1,
                })));

        var warning = Assert.IsType<ContextBudgetWarning>(events.Single(evt => evt is ContextBudgetWarning));
        Assert.Equal("gpt-5.4", warning.Result.Model);
        Assert.Equal(AgentContextBudgetLevel.Warning, warning.Result.Level);
        Assert.True(events.IndexOf(warning) < events.FindIndex(evt => evt is MessageStarted));
    }

    [Fact]
    public async Task RunAsync_WhenContextBudgetExceedsErrorThreshold_ThrowsBeforeRequest()
    {
        var client = new FakeLlmSdkClient
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4",
                    TokenLimits = new ModelTokenLimits { MaxPromptTokens = 1 },
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<AgentContextBudgetExceededException>(async () =>
            await CollectEventsAsync(AgentLoop.RunAsync(
                client,
                "Hello",
                CreateOptions(
                    model: "gpt-5.4",
                    contextBudget: new AgentContextBudgetOptions()))));

        Assert.Equal(AgentContextBudgetLevel.Error, exception.Result.Level);
        Assert.Null(client.LastCreateResponseStreamRequest);
    }

    private static AgentLoopOptions CreateOptions(
        IReadOnlyList<IAgentTool>? tools = null,
        int? maxTurns = null,
        string model = "gpt-4.1",
        string? instructions = null,
        double? temperature = null,
        ResponseReasoning? reasoning = null,
        AgentContextBudgetOptions? contextBudget = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? promptCacheKey = null,
        string? requestId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        int? timeoutMs = null,
        int? maxRetries = null,
        int? maxRetryDelayMs = null,
        Func<JsonNode, JsonNode?>? onPayload = null,
        Action<ResponseSnapshot>? onResponse = null) => new()
        {
            Model = model,
            Instructions = instructions,
            Tools = tools ?? [],
            MaxTurns = maxTurns,
            Temperature = temperature,
            Reasoning = reasoning,
            Headers = headers,
            PromptCacheKey = promptCacheKey,
            RequestId = requestId,
            CorrelationId = correlationId,
            Metadata = metadata,
            TimeoutMs = timeoutMs,
            MaxRetries = maxRetries,
            MaxRetryDelayMs = maxRetryDelayMs,
            OnPayload = onPayload,
            OnResponse = onResponse,
            ContextBudget = contextBudget,
        };

    private static JsonElement CreateObjectSchema(object properties, string[] required, bool additionalProperties = true) =>
        JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                properties,
                required,
                additionalProperties,
            },
            JsonDefaults.Web);

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
