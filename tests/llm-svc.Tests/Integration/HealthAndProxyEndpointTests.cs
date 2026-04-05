using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CopilotLlm.Core.Models;

namespace llm_svc.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class HealthAndProxyEndpointTests : IClassFixture<ResponsesWebApplicationFactory>
{
    private readonly ResponsesWebApplicationFactory _factory;

    public HealthAndProxyEndpointTests(ResponsesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_WhenAuthenticated_ReturnsHealthy()
    {
        _factory.Provider.IsAuthenticated = true;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task GetHealth_WhenNotAuthenticated_ReturnsDegraded()
    {
        _factory.Provider.IsAuthenticated = false;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
        Assert.False(doc.RootElement.GetProperty("authenticated").GetBoolean());

        // Reset for other tests sharing this fixture
        _factory.Provider.IsAuthenticated = true;
    }

    [Fact]
    public async Task PostChatCompletions_ChatOnlyModel_ProxiesPlainTextResponse()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5-mini",
                Name = "GPT-5 Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];

        var fakeResponse = new ChatCompletionResponse
        {
            Id = "chat_proxy_test",
            Model = "gpt-5-mini",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Proxied response",
                    },
                    FinishReason = "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = 3,
                CompletionTokens = 2,
                TotalTokens = 5,
            },
        };
        _factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(fakeResponse, JsonDefaults.Web), 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-5-mini",
            messages = new[] { new { role = "user", content = "Hello" } },
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("application/json", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("chat_proxy_test", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("Proxied response",
            doc.RootElement.GetProperty("choices")[0]
               .GetProperty("message")
               .GetProperty("content").GetString());
    }

    [Fact]
    public async Task PostChatCompletions_AlternateRoute_Works()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5-mini",
                Name = "GPT-5 Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];

        _factory.Provider.ChatCompletionsResult = new("{\"id\":\"alt\"}", 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/chat/completions", new
        {
            model = "gpt-5-mini",
            messages = new[] { new { role = "user", content = "Hi" } },
        });

        httpResponse.EnsureSuccessStatusCode();
        var body = await httpResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("alt", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetModels_AlternateRoute_Works()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "test-model",
                Name = "Test",
                OwnedBy = "test",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];

        using var client = _factory.CreateClient();
        var httpResponse = await client.GetAsync("/models");

        httpResponse.EnsureSuccessStatusCode();
        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<OpenAIModelListResponse>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        Assert.Single(response!.Data, m => m.Id == "test-model");
    }

}
