using System.Text.Json;
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
            createResponseAsync: (request, _) => Task.FromResult(response),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable([]));

        await SdkAskAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5.4-mini", "Be brief", false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello from sdk ask{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCreateResponseRequest);
        Assert.Equal("gpt-5.4-mini", client.LastCreateResponseRequest!.Model);
        Assert.Equal("Hi there", client.LastCreateResponseRequest.Input.GetString());
        Assert.Equal("Be brief", client.LastCreateResponseRequest.Instructions);
        Assert.Null(client.LastCreateResponseRequest.Stream);
    }

    [Fact]
    public async Task RunStreamingAsync_WritesOutputTextDeltas()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            createResponseAsync: (_, _) => throw new NotSupportedException(),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable(
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
        Assert.Equal("Hi there", client.LastCreateResponseStreamRequest.Input.GetString());
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
            createResponseAsync: (_, _) => Task.FromResult(response),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable([]));

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
            createResponseAsync: (_, _) => Task.FromResult(response),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable([]));

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
            createResponseAsync: (_, _) => Task.FromResult(response),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable([]));

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
            createResponseAsync: (_, _) => throw new NotSupportedException(),
            createResponseStreamAsync: (_, _) => ToAsyncEnumerable(
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

    private static async IAsyncEnumerable<ResponseStreamEvent> ToAsyncEnumerable(
        IEnumerable<ResponseStreamEvent> updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    private sealed class FakeLlmSdkClient(
        Func<CreateResponseRequest, CancellationToken, Task<Response>> createResponseAsync,
        Func<CreateResponseRequest, CancellationToken, IAsyncEnumerable<ResponseStreamEvent>> createResponseStreamAsync)
        : ILlmSdkClient
    {
        public CreateResponseRequest? LastCreateResponseRequest { get; private set; }

        public CreateResponseRequest? LastCreateResponseStreamRequest { get; private set; }

        public Task<Response> CreateResponseAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
        {
            LastCreateResponseRequest = request;
            return createResponseAsync(request, cancellationToken);
        }

        public Task<Response> CreateResponseAsync(string? model, string input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
        {
            LastCreateResponseStreamRequest = request;
            return createResponseStreamAsync(request, cancellationToken);
        }

        public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(string? model, string input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChatCompletionResponse> CreateChatCompletionAsync(string? model, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(string? model, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
