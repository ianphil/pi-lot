using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Int.Fakes;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

public sealed class RawResponsesSdkTests
{
    private readonly ITestOutputHelper _output;

    public RawResponsesSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CreateResponseAsync_WithFakeApiTools_ForwardsToolsAndReturnsFunctionCall()
    {
        var provider = new FakeModelProvider { Models = [CreateResponsesModel()] };
        provider.ResponsesResults.Enqueue(new ProxyHttpResult(
            JsonSerializer.Serialize(CreateToolCallResponse("""{"city":123}"""), JsonDefaults.Web),
            200));
        await using var services = SdkIntTestHost.CreateFakeApiProvider(provider);
        var client = services.GetRequiredService<ILlmSdkClient>();

        var response = await client.CreateResponseAsync(CreateToolRequest("fake-gpt"));

        var request = Assert.Single(provider.ResponsesRequests);
        Assert.Equal("fake-gpt", request.Model);
        Assert.Single(request.Tools ?? []);
        Assert.Equal("get_weather", request.Tools?[0].Name);
        Assert.Equal("function", request.ToolChoice?.GetProperty("type").GetString());
        var toolCall = Assert.IsType<ResponseFunctionCallItem>(Assert.Single(response.Output));
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("""{"city":123}""", toolCall.Arguments);
    }

    [Fact]
    public async Task CreateResponseStreamAsync_WithFakeApiTools_ForwardsToolsAndStreamsFunctionCall()
    {
        var provider = new FakeModelProvider { Models = [CreateResponsesModel()] };
        provider.ResponsesStreamResults.Enqueue(new ProxyStreamResult(
            null,
            200,
            chunks: ToAsyncEnumerable(SplitSseBody(ResponseSseSerializer.Serialize(CreateToolCallResponse("""{"city":123}"""))).ToArray())));
        await using var services = SdkIntTestHost.CreateFakeApiProvider(provider);
        var client = services.GetRequiredService<ILlmSdkClient>();

        var events = await CollectAsync(client.CreateResponseStreamAsync(CreateToolRequest("fake-gpt")));

        var request = Assert.Single(provider.ResponsesStreamRequests);
        Assert.True(request.Stream);
        Assert.Single(request.Tools ?? []);
        Assert.Equal("get_weather", request.Tools?[0].Name);
        Assert.Equal("function", request.ToolChoice?.GetProperty("type").GetString());
        var completed = Assert.Single(events.OfType<ResponseCompletedEvent>());
        var toolCall = Assert.IsType<ResponseFunctionCallItem>(Assert.Single(completed.Response.Output));
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("""{"city":123}""", toolCall.Arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CreateResponseAsync_WithLiveApiTools_ReturnsFunctionCall()
    {
        await using var services = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = services.GetRequiredService<ILlmSdkClient>();

        var response = await client.CreateResponseAsync(CreateToolRequest("gpt-5.4-mini"));
        _output.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        var toolCall = Assert.IsType<ResponseFunctionCallItem>(Assert.Single(response.Output));
        Assert.Equal("get_weather", toolCall.Name);
        using var arguments = JsonDocument.Parse(toolCall.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(arguments.RootElement.GetProperty("city").GetString()));
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CreateResponseStreamAsync_WithLiveApiTools_StreamsFunctionCall()
    {
        await using var services = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = services.GetRequiredService<ILlmSdkClient>();

        var events = await CollectAsync(client.CreateResponseStreamAsync(CreateToolRequest("gpt-5.4-mini")));

        _output.WriteLine(string.Join(Environment.NewLine, events.Select(static item => item.Type)));
        var completed = Assert.Single(events.OfType<ResponseCompletedEvent>());
        var toolCall = Assert.IsType<ResponseFunctionCallItem>(Assert.Single(completed.Response.Output));
        Assert.Equal("get_weather", toolCall.Name);
        using var arguments = JsonDocument.Parse(toolCall.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(arguments.RootElement.GetProperty("city").GetString()));
    }

    private static CreateResponseRequest CreateToolRequest(string model) => new()
    {
        Model = model,
        Input = JsonSerializer.SerializeToElement("Use get_weather for Seattle.", JsonDefaults.Web),
        Tools = [CreateWeatherTool()],
        ToolChoice = JsonSerializer.SerializeToElement(new
        {
            type = "function",
            name = "get_weather",
        }, JsonDefaults.Web),
        MaxOutputTokens = 128,
    };

    private static ResponseFunctionToolDefinition CreateWeatherTool()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "city" },
            additionalProperties = false,
            properties = new
            {
                city = new { type = "string" },
            },
        }, JsonDefaults.Web);

        return new ResponseFunctionToolDefinition
        {
            Name = "get_weather",
            Description = "Get current weather for a city.",
            Parameters = schema,
            Strict = true,
        };
    }

    private static ModelInfo CreateResponsesModel() => new()
    {
        Id = "fake-gpt",
        Object = "model",
        Name = "Fake GPT",
        Vendor = "Fake LLM",
        Version = "fake-gpt",
        SupportedEndpoints = ["/responses"],
    };

    private static Response CreateToolCallResponse(string argumentsJson) => new()
    {
        Id = "resp_raw_tools",
        Model = "fake-gpt",
        Output =
        [
            new ResponseFunctionCallItem
            {
                Id = "fc_raw_tools",
                CallId = "call_raw_tools",
                Name = "get_weather",
                Arguments = argumentsJson,
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
