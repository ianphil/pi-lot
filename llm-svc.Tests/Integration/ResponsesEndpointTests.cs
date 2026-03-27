using System.Net.Http.Json;
using System.Text.Json;
using LlmSvc.Core.Models;

namespace llm_svc.Tests.Integration;

public sealed class ResponsesEndpointTests : IClassFixture<ResponsesWebApplicationFactory>
{
    private readonly ResponsesWebApplicationFactory _factory;

    public ResponsesEndpointTests(ResponsesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostResponses_ReturnsCanonicalResponseBody()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat_456",
            Model = "claude-haiku-4.5",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Hello from endpoint test",
                    },
                    FinishReason = "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = 5,
                CompletionTokens = 4,
                TotalTokens = 9,
            },
        }, JsonDefaults.Web), 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Hi there",
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("application/json", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);

        Assert.NotNull(response);
        Assert.Equal("response", response!.Object);
        Assert.Equal("claude-haiku-4.5", response.Model);
        var message = Assert.IsType<ResponseMessageItem>(response.Output[0]);
        var text = Assert.IsType<ResponseOutputTextPart>(message.Content[0]);
        Assert.Equal("Hello from endpoint test", text.Text);
    }

    [Fact]
    public async Task PostResponses_WithStreaming_ReturnsEventStreamBody()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" there\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Hi there",
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: response.created", body);
        Assert.Contains("event: response.output_text.delta", body);
        Assert.Contains("\"text\":\"Hello there\"", body);
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task GetModels_ReportsUpstreamAndProxyEndpoints()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                Name = "Claude Haiku 4.5",
                OwnedBy = "github-copilot",
                SupportedEndpoints = ["/chat/completions", "/v1/messages"],
            },
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                Name = "GPT-5.4",
                OwnedBy = "github-copilot",
                SupportedEndpoints = ["/responses"],
            },
        ];

        using var client = _factory.CreateClient();
        var httpResponse = await client.GetAsync("/v1/models");

        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<OpenAIModelListResponse>(body, JsonDefaults.Web);

        Assert.NotNull(response);

        var claude = Assert.Single(response!.Data, model => model.Id == "claude-haiku-4.5");
        Assert.NotNull(claude.SupportedEndpoints);
        Assert.NotNull(claude.ProxySupportedEndpoints);
        Assert.Equal(["/chat/completions", "/v1/messages"], claude.SupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], claude.ProxySupportedEndpoints);

        var gpt = Assert.Single(response.Data, model => model.Id == "gpt-5.4");
        Assert.NotNull(gpt.SupportedEndpoints);
        Assert.NotNull(gpt.ProxySupportedEndpoints);
        Assert.Equal(["/responses"], gpt.SupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], gpt.ProxySupportedEndpoints);
    }

    private static async IAsyncEnumerable<string> AsAsyncChunks(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }
}
