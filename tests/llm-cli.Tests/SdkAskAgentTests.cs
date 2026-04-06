using System.Text.Json;
using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class SdkAskAgentTests
{
    [Fact]
    public async Task RunNonStreamingAsync_WritesOutputText()
    {
        using var writer = new StringWriter();
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
                        new ResponseOutputTextPart
                        {
                            Text = "Hello from sdk ask",
                        },
                    ],
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new ResponseCompletedEvent("response.completed", 1, response),
            ]));

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", "Be brief", false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello from sdk ask{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCreateResponseStreamRequest);
        Assert.Equal("gpt-5.4-mini", client.LastCreateResponseStreamRequest!.Model);
        Assert.Equal("message", client.LastCreateResponseStreamRequest.Input[0].GetProperty("type").GetString());
        Assert.Equal("Hi there", client.LastCreateResponseStreamRequest.Input[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Be brief", client.LastCreateResponseStreamRequest.Instructions);
        Assert.True(client.LastCreateResponseStreamRequest.Stream);
    }

    [Fact]
    public async Task RunStreamingAsync_WritesOutputTextDeltas()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new OutputTextDeltaEvent("response.output_text.delta", 1, "Hello ", 0, 0, "msg_123"),
                new OutputTextDeltaEvent("response.output_text.delta", 2, "world", 0, 0, "msg_123"),
                new ResponseCompletedEvent("response.completed", 3, new Response { Id = "resp_123" }),
            ]));

        await SdkAskAgent.RunStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", "Be brief", false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello world{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCreateResponseStreamRequest);
        Assert.Equal("gpt-5.4-mini", client.LastCreateResponseStreamRequest!.Model);
        Assert.Equal("message", client.LastCreateResponseStreamRequest.Input[0].GetProperty("type").GetString());
        Assert.Equal("Hi there", client.LastCreateResponseStreamRequest.Input[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Be brief", client.LastCreateResponseStreamRequest.Instructions);
        Assert.True(client.LastCreateResponseStreamRequest.Stream);
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenResponseFailed_WritesError()
    {
        using var writer = new StringWriter();
        var response = new Response
        {
            Id = "resp_123",
            Status = ResponseStatuses.Failed,
            Error = new ResponseError
            {
                Message = "upstream timeout",
                Type = "server_error",
            },
            Output = [],
        };

        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new ResponseFailedEvent("response.failed", 1, response),
            ]));

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Response failed: upstream timeout{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenResponseIncomplete_WritesTextAndWarning()
    {
        using var writer = new StringWriter();
        var response = new Response
        {
            Id = "resp_123",
            Status = ResponseStatuses.Incomplete,
            IncompleteDetails = new ResponseIncompleteDetails { Reason = "max_output_tokens" },
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "Partial output",
                        },
                    ],
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new ResponseIncompleteEvent("response.incomplete", 1, response),
            ]));

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", null, false),
            writer,
            CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Partial output", output);
        Assert.Contains("Response incomplete: max_output_tokens", output);
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenOutputTextIsMissing_WritesError()
    {
        using var writer = new StringWriter();
        var response = new Response
        {
            Id = "resp_123",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content = [],
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new ResponseCompletedEvent("response.completed", 1, response),
            ]));

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal($"No output text was returned.{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public async Task RunStreamingAsync_WhenResponseFails_WritesError()
    {
        using var writer = new StringWriter();
        var failedResponse = new Response
        {
            Id = "resp_123",
            Status = ResponseStatuses.Failed,
            Error = new ResponseError
            {
                Message = "boom",
                Type = "server_error",
            },
        };

        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(
            [
                new ResponseFailedEvent("response.failed", 1, failedResponse),
            ]));

        await SdkAskAgent.RunStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Response failed: boom{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public async Task RunNonStreamingAsync_WithTools_ExecutesFunctionCallAndWritesFinalText()
    {
        using var writer = new StringWriter();
        var requests = new List<CreateResponseRequest>();
        var firstResponse = new Response
        {
            Id = "resp_1",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_1",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "I'll fetch that.",
                        },
                    ],
                },
                new ResponseFunctionCallItem
                {
                    Id = "fc_1",
                    CallId = "call_1",
                    Name = FetchUrlTool.ToolName,
                    Arguments = """{"url":"https://example.com"}""",
                },
            ],
        };
        var secondResponse = new Response
        {
            Id = "resp_2",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_2",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "Example summary",
                        },
                    ],
                },
            ],
        };
        var queuedStreams = new Queue<ResponseStreamEvent[]>(
        [
            [new ResponseCompletedEvent("response.completed", 1, firstResponse)],
            [new ResponseCompletedEvent("response.completed", 2, secondResponse)],
        ]);
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(queuedStreams.Dequeue());
            });

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Summarize https://example.com", "gpt-5.4-mini", null, true),
            writer,
            CancellationToken.None,
            toolRegistry);

        Assert.Equal($"Example summary{Environment.NewLine}", writer.ToString());
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, toolRegistry.ExecutionCount);
        Assert.NotNull(requests[0].Tools);
        Assert.Single(requests[0].Tools!);
        Assert.Equal(4, requests[1].Input.GetArrayLength());
        Assert.Equal("function_call_output", requests[1].Input[3].GetProperty("type").GetString());
        Assert.Equal("call_1", requests[1].Input[3].GetProperty("call_id").GetString());
        Assert.Equal("""{"ok":true,"content":"Example page"}""", requests[1].Input[3].GetProperty("output").GetString());
    }

    [Fact]
    public async Task RunStreamingAsync_WithTools_BuffersIntermediateTurnAndWritesFinalText()
    {
        using var writer = new StringWriter();
        var requests = new List<CreateResponseRequest>();
        var firstResponse = new Response
        {
            Id = "resp_1",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_1",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "I'll fetch that.",
                        },
                    ],
                },
                new ResponseFunctionCallItem
                {
                    Id = "fc_1",
                    CallId = "call_1",
                    Name = FetchUrlTool.ToolName,
                    Arguments = """{"url":"https://example.com"}""",
                },
            ],
        };
        var secondResponse = new Response
        {
            Id = "resp_2",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_2",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "Streamed summary",
                        },
                    ],
                },
            ],
        };
        var queuedStreams = new Queue<ResponseStreamEvent[]>(
        [
            [
                new OutputTextDeltaEvent("response.output_text.delta", 1, "I'll fetch that.", 0, 0, "msg_1"),
                new ResponseCompletedEvent("response.completed", 2, firstResponse),
            ],
            [
                new OutputTextDeltaEvent("response.output_text.delta", 3, "Streamed ", 0, 0, "msg_2"),
                new OutputTextDeltaEvent("response.output_text.delta", 4, "summary", 0, 0, "msg_2"),
                new ResponseCompletedEvent("response.completed", 5, secondResponse),
            ],
        ]);
        var toolRegistry = new FakeToolRegistry("""{"ok":true,"content":"Example page"}""");
        var client = new FakeLlmSdkClient(
            createResponseStreamAsync: (request, _) =>
            {
                requests.Add(request);
                return AsyncEnumerableHelpers.ToAsyncEnumerable<ResponseStreamEvent>(queuedStreams.Dequeue());
            });

        await SdkAskAgent.RunStreamingAsync(
            client,
            new AskRequest("Summarize https://example.com", "gpt-5.4-mini", null, true),
            writer,
            CancellationToken.None,
            toolRegistry);

        Assert.Equal($"Streamed summary{Environment.NewLine}", writer.ToString());
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, toolRegistry.ExecutionCount);
        Assert.Equal(4, requests[1].Input.GetArrayLength());
    }
}
