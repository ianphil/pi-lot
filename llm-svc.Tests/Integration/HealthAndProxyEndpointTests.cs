using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmSvc.Core.Models;

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
