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

    private sealed class StubResponsesService(ResponseHttpResult result) : IResponsesService
    {
        public CreateResponseRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubChatCompletionsService(ResponseHttpResult result) : IChatCompletionsService
    {
        public ChatCompletionRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
