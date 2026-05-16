using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using LlmSdk;
using LlmSdk.Infrastructure;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

[Trait("Category", "Smoke")]
public sealed class ModelApiCaptureTests
{
    private const string DefaultCaptureFileName = "copilot-model-api-capture.json";
    private readonly ITestOutputHelper _output;

    public ModelApiCaptureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ModelsApi_WritesRawJsonCapture()
    {
        await using var provider = CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<CopilotClient>();
        var token = GetLoadedToken(client);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.enterprise.githubcopilot.com/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("copilot/1.0.11 (win32) term/service");
        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "copilot-developer-cli");

        using var response = await http.SendAsync(request);
        var rawJson = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(rawJson);
        var prettyJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        });
        var outputPath = GetCapturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, prettyJson);

        _output.WriteLine(outputPath);
        Assert.True(File.Exists(outputPath), $"Expected capture file to exist: {outputPath}");
    }

    private static ServiceProvider CreateAuthenticatedProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");
        return provider;
    }

    private static string GetLoadedToken(CopilotClient client)
    {
        var token = typeof(CopilotClient)
            .GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(client) as string;

        Assert.False(string.IsNullOrWhiteSpace(token), "Copilot token was not loaded.");
        return token!;
    }

    private static string GetCapturePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("LLM_SDK_INT_MODEL_CAPTURE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.Combine(AppContext.BaseDirectory, DefaultCaptureFileName);
    }
}
