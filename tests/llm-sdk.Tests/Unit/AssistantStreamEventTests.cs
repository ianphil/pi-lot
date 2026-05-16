using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Proxy;
using LlmSdk.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AssistantStreamEventTests
{
    [Fact]
    public async Task StreamAsync_WithResponsesPreference_EmitsUnifiedEventsWithUsageAndDone()
    {
        var response = new Response
        {
            Id = "resp_stream_context",
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content = [new ResponseOutputTextPart { Text = "Hello" }],
                },
            ],
            Usage = new ResponseUsage
            {
                InputTokens = 10,
                OutputTokens = 5,
                TotalTokens = 15,
                InputTokensDetails = new InputTokensDetails { CachedTokens = 2 },
            },
        };
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            SplitSseBody(ResponseSseSerializer.Serialize(response)).ToArray())));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.Responses }));

        Assert.IsType<StreamStart>(events[0]);
        Assert.Contains(events, static e => e is TextDelta { Text: "Hello" });
        Assert.Contains(events, static e => e is UsageEvent { Usage.InputTokens: 10, Usage.OutputTokens: 5, Usage.CacheReadTokens: 2 });
        var done = Assert.Single(events.OfType<StreamDone>());
        Assert.Equal(new Usage(10, 5, CacheReadTokens: 2), done.FinalMessage.Usage);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task StreamAsync_WithChatCompletionsPreference_EmitsUnifiedEventsWithDone()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            SerializeChatChunk(new ChatCompletionChunk
            {
                Id = "chatcmpl_stream_context",
                Model = "gpt-5.4-mini",
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatChunkDelta { Role = "assistant", Content = "Hel" },
                    },
                ],
            }),
            SerializeChatChunk(new ChatCompletionChunk
            {
                Id = "chatcmpl_stream_context",
                Model = "gpt-5.4-mini",
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatChunkDelta { Content = "lo" },
                        FinishReason = "stop",
                    },
                ],
                Usage = new UsageInfo { PromptTokens = 8, CompletionTokens = 2, TotalTokens = 10 },
            }),
            "data: [DONE]\n\n")));
        var client = CreateClient(chatService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.ChatCompletions }));

        Assert.IsType<StreamStart>(events[0]);
        Assert.Equal(["Hel", "lo"], events.OfType<TextDelta>().Select(static e => e.Text));
        Assert.Contains(events, static e => e is UsageEvent { Usage.InputTokens: 8, Usage.OutputTokens: 2 });
        var done = Assert.Single(events.OfType<StreamDone>());
        Assert.Equal("Hello", Assert.IsType<TextContent>(Assert.Single(done.FinalMessage.Content)).Text);
        Assert.Equal(new Usage(8, 2), done.FinalMessage.Usage);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task StreamAsync_WithResponsesError_EmitsOneStreamErrorAndNoDone()
    {
        var failed = new Response
        {
            Id = "resp_failed",
            Model = "gpt-5.4-mini",
            Status = ResponseStatuses.Failed,
            Error = new ResponseError
            {
                Message = "Upstream failed.",
                Type = ErrorTypes.ServerError,
                Code = ErrorCodes.StreamError,
            },
        };
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            ResponseSseSerializer.SerializeEvent("response.failed", new
            {
                type = "response.failed",
                sequence_number = 1,
                response = failed,
            }),
            ResponseSseSerializer.SerializeDone())));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" }));

        var error = Assert.Single(events.OfType<StreamError>());
        Assert.Equal("Upstream failed.", error.Message);
        Assert.Empty(events.OfType<StreamDone>());
    }

    [Fact]
    public async Task StreamAsync_WhenResponseStreamIsCanceled_ReturnsAbortedPartialByDefault()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ThrowAfter(
            new OperationCanceledException("Canceled by caller."),
            ResponseTextDelta("Hel"),
            ResponseTextDelta("lo"))));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" }));

        var done = Assert.Single(events.OfType<StreamDone>());
        Assert.Equal(StopReason.Aborted, done.FinalMessage.StopReason);
        Assert.Equal("Hello", string.Concat(done.FinalMessage.Content.OfType<TextContent>().Select(static c => c.Text)));
        Assert.Empty(events.OfType<StreamError>());
    }

    [Fact]
    public async Task StreamAsync_WhenResponseStreamFails_ReturnsErrorPartialByDefault()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ThrowAfter(
            new HttpRequestException("stream disconnected"),
            ResponseTextDelta("Hel"),
            ResponseTextDelta("lo"))));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" }));

        var error = Assert.Single(events.OfType<StreamError>());
        Assert.Equal("stream disconnected", error.Message);
        Assert.Equal(StopReason.Error, error.PartialMessage.StopReason);
        Assert.Equal("Hello", string.Concat(error.PartialMessage.Content.OfType<TextContent>().Select(static c => c.Text)));
        Assert.Equal("stream disconnected", error.PartialMessage.ErrorMessage);
        Assert.Empty(events.OfType<StreamDone>());
    }

    [Fact]
    public async Task StreamAsync_WhenResponseStreamEmitsMultipleErrors_EmitsOneTerminalEvent()
    {
        var failed = new Response
        {
            Id = "resp_failed",
            Model = "gpt-5.4-mini",
            Status = ResponseStatuses.Failed,
            Error = new ResponseError { Message = "Response failed.", Type = ErrorTypes.ServerError },
        };
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            ResponseTextDelta("Partial"),
            ResponseSseSerializer.SerializeEvent("error", new
            {
                type = "error",
                sequence_number = 2,
                error = new { message = "Stream failed.", type = ErrorTypes.ServerError },
            }),
            ResponseSseSerializer.SerializeEvent("response.failed", new
            {
                type = "response.failed",
                sequence_number = 3,
                response = failed,
            }))));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" }));

        var error = Assert.Single(events.OfType<StreamError>());
        Assert.Equal("Stream failed.", error.Message);
        Assert.Equal("Partial", Assert.IsType<TextContent>(Assert.Single(error.PartialMessage.Content)).Text);
        Assert.Empty(events.OfType<StreamDone>());
    }

    [Fact]
    public async Task StreamAsync_WhenResponseStreamEndsWithoutTerminal_ReturnsErrorPartial()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            ResponseTextDelta("Partial"))));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.StreamAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" }));

        var error = Assert.Single(events.OfType<StreamError>());
        Assert.Equal("Response stream ended before a terminal event.", error.Message);
        Assert.Equal(StopReason.Error, error.PartialMessage.StopReason);
        Assert.Equal("Partial", Assert.IsType<TextContent>(Assert.Single(error.PartialMessage.Content)).Text);
        Assert.Empty(events.OfType<StreamDone>());
    }

    [Fact]
    public async Task StreamAsync_WithThrowAbortMode_PreservesCancellationException()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ThrowAfter(
            new OperationCanceledException("Canceled by caller."),
            ResponseTextDelta("Hel"))));
        var client = CreateClient(responsesService: service);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CollectAsync(client.StreamAsync(
                new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
                new CompletionOptions { Model = "gpt-5.4-mini", AbortMode = AbortMode.Throw })));
    }

    [Fact]
    public async Task CompleteAsync_WhenResponseStreamFails_ReturnsErrorPartialByDefault()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ThrowAfter(
            new HttpRequestException("stream disconnected"),
            ResponseTextDelta("Partial"))));
        var client = CreateClient(responsesService: service);

        var message = await client.CompleteAsync(
            new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
            new CompletionOptions { Model = "gpt-5.4-mini" });

        Assert.Equal(StopReason.Error, message.StopReason);
        Assert.Equal("Partial", Assert.IsType<TextContent>(Assert.Single(message.Content)).Text);
        Assert.Equal("stream disconnected", message.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithThrowAbortMode_PreservesNonStreamingBehavior()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ThrowAfter(
            new HttpRequestException("stream disconnected"),
            ResponseTextDelta("Partial"))));
        var client = CreateClient(responsesService: service);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.CompleteAsync(
                new Context { Messages = [new UserMessage([new TextContent("Hello")])] },
                new CompletionOptions { Model = "gpt-5.4-mini", AbortMode = AbortMode.Throw }));
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var items = new List<T>();
        await foreach (var value in values)
        {
            items.Add(value);
        }

        return items;
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(params string[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<string> ThrowAfter(Exception exception, params string[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }

        throw exception;
    }

    private static string ResponseTextDelta(string text) =>
        ResponseSseSerializer.SerializeEvent("response.output_text.delta", new
        {
            type = "response.output_text.delta",
            sequence_number = 1,
            item_id = "msg_123",
            output_index = 0,
            content_index = 0,
            delta = text,
        });

    private static IEnumerable<string> SplitSseBody(string body)
    {
        foreach (var chunk in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            yield return $"{chunk}\n\n";
        }
    }

    private static string SerializeChatChunk(ChatCompletionChunk chunk) =>
        $"data: {System.Text.Json.JsonSerializer.Serialize(chunk, JsonDefaults.Web)}\n\n";

    private static LlmSdkClient CreateClient(
        StubResponsesService? responsesService = null,
        StubChatCompletionsService? chatService = null)
    {
        return new LlmSdkClient(
            responsesService ?? new StubResponsesService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            chatService ?? new StubChatCompletionsService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            new ModelListService(new FakeModelProvider()),
            Options.Create(new LlmSdkOptions()));
    }

}
