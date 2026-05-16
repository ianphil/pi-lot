using System.Text.Json;
using LlmSdk;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

/// <summary>
/// Live SDK integration tests that call the actual Copilot API.
/// Run with: dotnet test tests/llm-sdk.Int/llm-sdk.Int.csproj --filter Category=Smoke
/// Requires valid Copilot credentials via COPILOT_TOKEN or the configured local credential store.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SdkHeaderRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public SdkHeaderRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SendResponsesAsync_WithRequestAndCorrelationHeaders_ReturnsObservedUpstreamHeaders()
    {
        var requestId = "sdk-int-" + Guid.NewGuid().ToString("N");
        var correlationId = "sdk-correlation-" + Guid.NewGuid().ToString("N");

        await using var provider = CreateProvider();
        var auth = provider.GetRequiredService<IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");

        var modelProvider = provider.GetRequiredService<IModelProvider>();
        var result = await modelProvider.SendResponsesAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Input = JsonSerializer.SerializeToElement("Reply with exactly: hello", JsonDefaults.Web),
            Headers = new Dictionary<string, string>
            {
                ["X-Request-Id"] = requestId,
                ["X-Correlation-Id"] = correlationId,
            },
            TimeoutMs = 60000,
        });

        _output.WriteLine($"Sent X-Request-Id: {requestId}");
        _output.WriteLine($"Sent X-Correlation-Id: {correlationId}");
        _output.WriteLine($"Status: {result.StatusCode}");
        foreach (var header in result.Headers.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"{header.Key}: {string.Join("|", header.Value)}");
        }

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(requestId, Assert.Single(GetRequiredHeader(result.Headers, "X-Request-Id")));
        Assert.NotEmpty(Assert.Single(GetRequiredHeader(result.Headers, "X-GitHub-Request-Id")));
        Assert.DoesNotContain(result.Headers, header =>
            string.Equals(header.Key, "X-Correlation-Id", StringComparison.OrdinalIgnoreCase) &&
            header.Value.Contains(correlationId, StringComparer.Ordinal));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        return services.BuildServiceProvider();
    }

    private static string[] GetRequiredHeader(IReadOnlyDictionary<string, string[]> headers, string name)
    {
        Assert.True(headers.TryGetValue(name, out var values), $"Expected response header '{name}' to be present.");
        return values;
    }
}
