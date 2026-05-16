using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Int.Fakes;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

public sealed class PortableContextSdkTests
{
    private readonly ITestOutputHelper _output;

    public PortableContextSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CompleteAsync_WithFakeApi_ReturnsAssistantMessageWithUsage()
    {
        var provider = new FakeModelProvider { Models = [CreateResponsesModel()] };
        provider.ResponsesResults.Enqueue(new ProxyHttpResult(
            JsonSerializer.Serialize(CreateTextResponse("Hello from fake.", new ResponseUsage
            {
                InputTokens = 10,
                OutputTokens = 4,
                TotalTokens = 14,
                InputTokensDetails = new InputTokensDetails { CachedTokens = 3 },
            }), JsonDefaults.Web),
            200));
        await using var services = SdkIntTestHost.CreateFakeApiProvider(provider);
        var client = services.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateContext("Say hello."), new CompletionOptions
        {
            Model = "fake-gpt",
            MaxOutputTokens = 32,
        });

        Assert.Equal("Hello from fake.", Assert.IsType<TextContent>(Assert.Single(message.Content)).Text);
        Assert.Equal(new Usage(10, 4, CacheReadTokens: 3), message.Usage);
        var request = Assert.Single(provider.ResponsesRequests);
        Assert.Equal("fake-gpt", request.Model);
        Assert.Equal("Be concise.", request.Instructions);
        Assert.Equal(32, request.MaxOutputTokens);
    }

    [Fact]
    public async Task StreamAsync_WithFakeApi_ReturnsUnifiedEventsWithUsage()
    {
        var provider = new FakeModelProvider { Models = [CreateResponsesModel()] };
        provider.ResponsesStreamResults.Enqueue(new ProxyStreamResult(
            null,
            200,
            chunks: ToAsyncEnumerable(SplitSseBody(ResponseSseSerializer.Serialize(CreateTextResponse("Hello stream.", new ResponseUsage
            {
                InputTokens = 7,
                OutputTokens = 3,
                TotalTokens = 10,
            }))).ToArray())));
        await using var services = SdkIntTestHost.CreateFakeApiProvider(provider);
        var client = services.GetRequiredService<ILlmSdkClient>();

        var events = await CollectAsync(client.StreamAsync(CreateContext("Say hello."), new CompletionOptions
        {
            Model = "fake-gpt",
        }));

        Assert.IsType<StreamStart>(events[0]);
        Assert.Contains(events, static item => item is TextDelta { Text: "Hello stream." });
        Assert.Contains(events, static item => item is UsageEvent { Usage.InputTokens: 7, Usage.OutputTokens: 3 });
        var done = Assert.Single(events.OfType<StreamDone>());
        Assert.Equal("Hello stream.", Assert.IsType<TextContent>(Assert.Single(done.FinalMessage.Content)).Text);
        Assert.Equal(new Usage(7, 3), done.FinalMessage.Usage);
        var request = Assert.Single(provider.ResponsesStreamRequests);
        Assert.True(request.Stream);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CompleteAsync_WithLiveApi_ReturnsAssistantMessageWithUsage()
    {
        await using var services = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = services.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateContext("Reply with exactly: hello"), new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            MaxOutputTokens = 32,
        });

        var text = string.Concat(message.Content.OfType<TextContent>().Select(static content => content.Text)).Trim();
        _output.WriteLine(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(message.Usage);
        Assert.True(message.Usage.InputTokens > 0);
        Assert.True(message.Usage.OutputTokens > 0);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task StreamAsync_WithLiveApi_ReturnsUnifiedEventsWithUsage()
    {
        await using var services = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = services.GetRequiredService<ILlmSdkClient>();

        var events = await CollectAsync(client.StreamAsync(CreateContext("Reply with exactly: hello"), new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            MaxOutputTokens = 32,
        }));

        var text = string.Concat(events.OfType<TextDelta>().Select(static content => content.Text)).Trim();
        var done = Assert.Single(events.OfType<StreamDone>());
        _output.WriteLine(string.Join(Environment.NewLine, events.Select(static item => item.GetType().Name)));
        _output.WriteLine(text);

        Assert.IsType<StreamStart>(events[0]);
        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, static item => item is UsageEvent);
        Assert.NotNull(done.FinalMessage.Usage);
        Assert.True(done.FinalMessage.Usage.InputTokens > 0);
        Assert.True(done.FinalMessage.Usage.OutputTokens > 0);
    }

    private static Context CreateContext(string prompt) => new()
    {
        System = "Be concise.",
        Messages = [new UserMessage([new TextContent(prompt)])],
    };

    private static ModelInfo CreateResponsesModel() => new()
    {
        Id = "fake-gpt",
        Object = "model",
        Name = "Fake GPT",
        Vendor = "Fake LLM",
        Version = "fake-gpt",
        SupportedEndpoints = ["/responses"],
    };

    private static Response CreateTextResponse(string text, ResponseUsage usage) => new()
    {
        Id = "resp_context_int",
        Model = "fake-gpt",
        Usage = usage,
        Output =
        [
            new ResponseMessageItem
            {
                Id = "msg_context_int",
                Content = [new ResponseOutputTextPart { Text = text }],
            },
        ],
    };

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
}
