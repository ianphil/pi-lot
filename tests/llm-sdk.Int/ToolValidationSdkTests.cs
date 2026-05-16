using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Int.Fakes;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

public sealed class ToolValidationSdkTests
{
    private readonly ITestOutputHelper _output;

    public ToolValidationSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CompleteAsync_WithFakeApiInvalidToolArguments_ReturnsErrorToolResult()
    {
        var provider = new FakeModelProvider { Models = [CreateResponsesModel()] };
        provider.ResponsesResults.Enqueue(new ProxyHttpResult(
            JsonSerializer.Serialize(CreateToolCallResponse("""{"city":123}"""), JsonDefaults.Web),
            200));
        await using var services = SdkIntTestHost.CreateFakeApiProvider(provider);
        var client = services.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateWeatherContext(), new CompletionOptions
        {
            Model = "fake-gpt",
            ToolChoice = ToolChoice.Function("get_weather"),
            AbortMode = AbortMode.Throw,
        });

        var result = Assert.IsType<ToolResultContent>(Assert.Single(message.Content));
        Assert.Equal("call_1", result.ToolCallId);
        Assert.True(result.IsError);
        Assert.Contains("city must be string", result.Output);
        Assert.Single(provider.ResponsesRequests);
        Assert.Single(provider.ResponsesRequests[0].Tools ?? []);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CompleteAsync_WithLiveApiValidToolArguments_ReturnsValidatedToolCall()
    {
        await using var provider = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateWeatherContext(), new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            ToolChoice = ToolChoice.Function("get_weather"),
            Temperature = 0,
            MaxOutputTokens = 128,
        });

        _output.WriteLine(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(message.Content));
        Assert.Equal("get_weather", toolCall.Name);
        Assert.True(ToolValidator.Validate(CreateWeatherTool(), toolCall.ArgumentsJson).IsValid);
    }

    private static Context CreateWeatherContext() => new()
    {
        System = "Use tools when asked for weather.",
        Messages = [new UserMessage([new TextContent("Use get_weather for Seattle.")])],
        Tools = [CreateWeatherTool()],
    };

    private static ToolDefinition CreateWeatherTool()
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

        return new ToolDefinition("get_weather", "Get current weather for a city.", schema, Strict: true);
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
        Id = "resp_tool_validation",
        Model = "fake-gpt",
        Status = ResponseStatuses.Completed,
        Output =
        [
            new ResponseFunctionCallItem
            {
                Id = "item_1",
                CallId = "call_1",
                Name = "get_weather",
                Arguments = argumentsJson,
                Status = "completed",
            },
        ],
    };
}
