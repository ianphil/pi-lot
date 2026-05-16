using LlmSdk.Core.Models;
using LlmSdk.Int.Fakes;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmSdk.Int;

internal static class SdkIntTestHost
{
    public static ServiceProvider CreateAuthenticatedProvider()
    {
        var services = CreateBaseServices();
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");
        return provider;
    }

    public static ServiceProvider CreateFakeApiProvider(params ModelInfo[] models)
    {
        var services = CreateBaseServices();
        services.RemoveAll<IModelProvider>();
        services.AddSingleton<IModelProvider>(new FakeModelProvider { Models = models });
        return services.BuildServiceProvider();
    }

    private static ServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        return services;
    }
}
