using System.Text.Json;
using llm_cli.Agents;
using llm_cli.Tests.Fakes;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Tests;

[Trait("Category", "Unit")]
public sealed class SdkChatAgentTests
{
    [Fact]
    public async Task RunNonStreamingAsync_WritesMessageText()
    {
        using var writer = new StringWriter();
        var response = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Content = JsonDocument.Parse("\"Hello from sdk chat\"").RootElement.Clone(),
                    },
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createChatCompletionAsync: (_, _) => Task.FromResult(response));

        await SdkChatAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", "Be brief", false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello from sdk chat{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCreateChatCompletionRequest);
        Assert.Equal("gpt-5-mini", client.LastCreateChatCompletionRequest!.Model);
        Assert.Equal(2, client.LastCreateChatCompletionRequest.Messages!.Length);
        Assert.Equal("system", client.LastCreateChatCompletionRequest.Messages[0].Role);
        Assert.Equal("Be brief", client.LastCreateChatCompletionRequest.Messages[0].Content);
        Assert.Equal("user", client.LastCreateChatCompletionRequest.Messages[1].Role);
        Assert.Equal("Hi there", client.LastCreateChatCompletionRequest.Messages[1].Content);
        Assert.Null(client.LastCreateChatCompletionRequest.Stream);
    }

    [Fact]
    public async Task RunStreamingAsync_WritesDeltaContentAndSkipsNulls()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            createChatCompletionStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ChatCompletionChunk>(
            [
                new ChatCompletionChunk
                {
                    Choices = [],
                },
                new ChatCompletionChunk
                {
                    Choices =
                    [
                        new ChatChunkChoice
                        {
                            Delta = null,
                        },
                    ],
                },
                new ChatCompletionChunk
                {
                    Choices =
                    [
                        new ChatChunkChoice
                        {
                            Delta = new ChatChunkDelta
                            {
                                Content = "Hello ",
                            },
                        },
                    ],
                },
                new ChatCompletionChunk
                {
                    Choices =
                    [
                        new ChatChunkChoice
                        {
                            Delta = new ChatChunkDelta
                            {
                                Content = "world",
                            },
                        },
                    ],
                },
            ]));

        await SdkChatAgent.RunStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", "Be brief", false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Hello world{Environment.NewLine}", writer.ToString());
        Assert.NotNull(client.LastCreateChatCompletionStreamRequest);
        Assert.Equal("gpt-5-mini", client.LastCreateChatCompletionStreamRequest!.Model);
        Assert.Equal(2, client.LastCreateChatCompletionStreamRequest.Messages!.Length);
        Assert.True(client.LastCreateChatCompletionStreamRequest.Stream);
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenFinishReasonIsNotStop_WritesWarning()
    {
        using var writer = new StringWriter();
        var response = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Content = JsonDocument.Parse("\"Partial answer\"").RootElement.Clone(),
                    },
                    FinishReason = "length",
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createChatCompletionAsync: (_, _) => Task.FromResult(response));

        await SdkChatAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", null, false),
            writer,
            CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Partial answer", output);
        Assert.Contains("Finish reason: length", output);
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenFinishReasonIsStop_NoWarning()
    {
        using var writer = new StringWriter();
        var response = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Content = JsonDocument.Parse("\"Complete answer\"").RootElement.Clone(),
                    },
                    FinishReason = "stop",
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createChatCompletionAsync: (_, _) => Task.FromResult(response));

        await SdkChatAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal($"Complete answer{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public async Task RunStreamingAsync_WhenFinishReasonIsNotStop_WritesWarning()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            createChatCompletionStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ChatCompletionChunk>(
            [
                new ChatCompletionChunk
                {
                    Choices =
                    [
                        new ChatChunkChoice
                        {
                            Delta = new ChatChunkDelta { Content = "Partial" },
                        },
                    ],
                },
                new ChatCompletionChunk
                {
                    Choices =
                    [
                        new ChatChunkChoice
                        {
                            Delta = new ChatChunkDelta { Content = null },
                            FinishReason = "length",
                        },
                    ],
                },
            ]));

        await SdkChatAgent.RunStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", null, false),
            writer,
            CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Partial", output);
        Assert.Contains("Finish reason: length", output);
    }

    [Fact]
    public async Task RunStreamingAsync_WhenStreamIsEmpty_WritesNewlineOnly()
    {
        using var writer = new StringWriter();
        var client = new FakeLlmSdkClient(
            createChatCompletionStreamAsync: (_, _) => AsyncEnumerableHelpers.ToAsyncEnumerable<ChatCompletionChunk>([]));

        await SdkChatAgent.RunStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal(Environment.NewLine, writer.ToString());
    }

    [Fact]
    public async Task RunNonStreamingAsync_WhenMessageTextIsMissing_WritesError()
    {
        using var writer = new StringWriter();
        var response = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Content = JsonDocument.Parse("{}").RootElement.Clone(),
                    },
                },
            ],
        };

        var client = new FakeLlmSdkClient(
            createChatCompletionAsync: (_, _) => Task.FromResult(response));

        await SdkChatAgent.RunNonStreamingAsync(
            client,
            new AskRequest("Hi there", "gpt-5-mini", null, false),
            writer,
            CancellationToken.None);

        Assert.Equal($"No message text was returned.{Environment.NewLine}", writer.ToString());
    }
}
