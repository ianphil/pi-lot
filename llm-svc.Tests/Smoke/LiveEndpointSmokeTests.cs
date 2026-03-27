using System.Net.Http.Json;
using System.Text.Json;

namespace llm_svc.Tests.Smoke;

/// <summary>
/// Live smoke tests that hit a running instance at localhost:5100.
/// Run with: dotnet test --filter Category=Smoke
/// Requires the service to be running with valid Copilot credentials.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class LiveEndpointSmokeTests : IDisposable
{
    private readonly HttpClient _client;

    public LiveEndpointSmokeTests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5100") };
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Health_ReturnsAuthenticated()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Models_ReturnsNonEmptyList()
    {
        var response = await _client.GetAsync("/v1/models");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("list", doc.RootElement.GetProperty("object").GetString());
        Assert.NotEqual(0, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Responses_ReturnsCompletedResponse()
    {
        var response = await _client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Respond with only the word 'pong'.",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.Equal("response", doc.RootElement.GetProperty("object").GetString());
        Assert.Equal("completed", doc.RootElement.GetProperty("status").GetString());

        var output = doc.RootElement.GetProperty("output");
        Assert.NotEqual(0, output.GetArrayLength());
    }
}
