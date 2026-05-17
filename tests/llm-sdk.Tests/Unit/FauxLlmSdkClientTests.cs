using System.Reflection;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Tests.Fakes;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FauxLlmSdkClientTests
{
    private const string UnsupportedMessage = "FauxLlmSdkClient supports the portable Context API. Use CompleteAsync or StreamAsync.";

    [Fact]
    public void PublicSdkClientMethods_AreExplicitlyDeclaredByFauxClient()
    {
        var fauxMethods = typeof(FauxLlmSdkClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        var missingMethods = typeof(ILlmSdkClient)
            .GetMethods()
            .Where(interfaceMethod => !fauxMethods.Any(fauxMethod => HasSameSignature(fauxMethod, interfaceMethod)))
            .Select(static method => method.ToString())
            .ToArray();

        Assert.Empty(missingMethods);
    }

    [Fact]
    public async Task CompleteAsync_WithScriptedToolCallThenText_ReturnsAggregatedAssistantMessageAndRecordsRequests()
    {
        var client = new FauxLlmSdkClient(
        [
            FauxResponse.ToolCall("get_weather", "{\"city\":\"London\"}", id: "call_1"),
            FauxResponse.Text("It is 21C.", new Usage(10, 4)),
        ]);

        var first = await client.CompleteAsync(new Context
        {
            Messages = [new UserMessage([new TextContent("Weather?")])],
        });
        var second = await client.CompleteAsync(new Context
        {
            Messages = [new ToolMessage("call_1", [new ToolResultContent("call_1", "{\"temperature\":21}")])],
        });

        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(first.Content));
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal(StopReason.ToolUse, first.StopReason);
        Assert.Equal("It is 21C.", Assert.IsType<TextContent>(Assert.Single(second.Content)).Text);
        Assert.Equal(new Usage(10, 4), second.Usage);
        Assert.Equal(2, client.RecordedRequests.Count);
    }

    [Fact]
    public async Task CompleteAsync_WithRepeatedCacheSession_SimulatesCacheHitUsage()
    {
        var client = new FauxLlmSdkClient(
        [
            FauxResponse.Text("First", new Usage(10, 2)),
            FauxResponse.Text("Second", new Usage(10, 2)),
        ]);
        var options = new CompletionOptions
        {
            Cache = CacheRetention.Short,
            SessionId = "session-123",
        };

        var first = await client.CompleteAsync(new Context(), options);
        var second = await client.CompleteAsync(new Context(), options);

        Assert.Equal(0, first.Usage?.CacheReadTokens);
        Assert.Equal(10, second.Usage?.CacheReadTokens);
    }

    [Fact]
    public async Task StreamAsync_WithScriptedResponse_YieldsEventsInOrderAndRecordsRequest()
    {
        var scripted = FauxResponse.Text("Hello", new Usage(3, 2));
        var client = new FauxLlmSdkClient([scripted]);

        var events = await CollectAsync(client.StreamAsync(new Context
        {
            Messages = [new UserMessage([new TextContent("Hi")])],
        }));

        Assert.Collection(events,
            static e => Assert.IsType<StreamStart>(e),
            static e => Assert.Equal("Hello", Assert.IsType<TextDelta>(e).Text),
            static e => Assert.Equal(new Usage(3, 2), Assert.IsType<UsageEvent>(e).Usage),
            static e => Assert.Equal("Hello", Assert.IsType<TextContent>(Assert.Single(Assert.IsType<StreamDone>(e).FinalMessage.Content)).Text));
        Assert.Single(client.RecordedRequests);
    }

    [Fact]
    public async Task CompleteAsync_WithMissingScriptedResponse_ThrowsClearInvalidOperationException()
    {
        var client = new FauxLlmSdkClient([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new Context()));

        Assert.Equal("FauxLlmSdkClient has no scripted response for call 1.", exception.Message);
    }

    [Fact]
    public async Task ModelInfoMethods_WithConfiguredModels_ReturnCannedModels()
    {
        var expected = new ModelInfo
        {
            Id = "fake-gpt-5.5",
            Name = "Fake GPT 5.5",
            SupportedEndpoints = ["/responses"],
        };
        var client = new FauxLlmSdkClient([], [expected]);

        var models = await client.ListModelsAsync();
        var model = await client.GetModelAsync("FAKE-GPT-5.5");

        Assert.Same(expected, Assert.Single(models));
        Assert.Same(expected, model);
    }

    [Fact]
    public async Task GetModelAsync_WithUnknownModel_ReturnsConservativeDefaults()
    {
        var client = new FauxLlmSdkClient([]);

        var model = await client.GetModelAsync("unknown-model");

        Assert.Equal("unknown-model", model.Id);
        Assert.Equal("unknown-model", model.DisplayName);
        Assert.Empty(model.SupportedEndpoints);
    }

    [Fact]
    public async Task UnsupportedLowLevelMethods_ThrowClearNotSupportedException()
    {
        var client = new FauxLlmSdkClient([]);

        var createResponse = await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.CreateResponseAsync(new CreateResponseRequest()));
        var createResponseString = await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.CreateResponseAsync("model", "input"));
        var createChatCompletion = await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.CreateChatCompletionAsync(new ChatCompletionRequest()));
        var createChatCompletionString = await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.CreateChatCompletionAsync("model", "message"));
        var createResponseStream = Assert.Throws<NotSupportedException>(() =>
            client.CreateResponseStreamAsync(new CreateResponseRequest()));
        var createResponseStreamString = Assert.Throws<NotSupportedException>(() =>
            client.CreateResponseStreamAsync("model", "input"));
        var createChatCompletionStream = Assert.Throws<NotSupportedException>(() =>
            client.CreateChatCompletionStreamAsync(new ChatCompletionRequest()));
        var createChatCompletionStreamString = Assert.Throws<NotSupportedException>(() =>
            client.CreateChatCompletionStreamAsync("model", "message"));

        Assert.All(
        [
            createResponse,
            createResponseString,
            createChatCompletion,
            createChatCompletionString,
            createResponseStream,
            createResponseStreamString,
            createChatCompletionStream,
            createChatCompletionStreamString,
        ], exception => Assert.Equal(UnsupportedMessage, exception.Message));
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

    private static bool HasSameSignature(MethodInfo method, MethodInfo interfaceMethod)
    {
        if (method.Name != interfaceMethod.Name || method.ReturnType != interfaceMethod.ReturnType)
        {
            return false;
        }

        var methodParameters = method.GetParameters();
        var interfaceParameters = interfaceMethod.GetParameters();
        return methodParameters.Length == interfaceParameters.Length &&
               methodParameters.Zip(interfaceParameters)
                   .All(static pair => pair.First.ParameterType == pair.Second.ParameterType);
    }
}
