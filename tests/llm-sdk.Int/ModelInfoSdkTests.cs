using System.Text.Json;
using LlmSdk;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

[Trait("Category", "Smoke")]
public sealed class ModelInfoSdkTests
{
    private readonly ITestOutputHelper _output;

    public ModelInfoSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetModelAsync_WithLiveApi_ReturnsModelInfo()
    {
        await using var provider = CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<ILlmSdkClient>();

        var liveModels = await client.ListModelsAsync();
        Assert.NotEmpty(liveModels);

        var modelId = liveModels[0].Id;
        var model = await client.GetModelAsync(modelId);

        _output.WriteLine(JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        Assert.Equal(modelId, model.Id);
        Assert.NotEmpty(model.SupportedEndpoints);
        Assert.NotEmpty(model.ProxySupportedEndpoints);
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
}
