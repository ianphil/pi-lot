using CopilotLlm;
using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using CopilotLlm.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCopilotLlm_RegistersLibraryServices()
    {
        using var provider = CreateProvider();

        var client = provider.GetRequiredService<CopilotClient>();

        Assert.Same(client, provider.GetRequiredService<IAuthProvider>());
        Assert.Same(client, provider.GetRequiredService<IModelProvider>());
        Assert.IsType<ResponsesService>(provider.GetRequiredService<IResponsesService>());
        Assert.IsType<ChatCompletionsService>(provider.GetRequiredService<IChatCompletionsService>());
        Assert.IsType<ChatCompletionsTranslator>(provider.GetRequiredService<ChatCompletionsTranslator>());
        Assert.IsType<ChatCompletionsStreamTranslator>(provider.GetRequiredService<ChatCompletionsStreamTranslator>());
        Assert.IsType<ResponsesStreamToChatTranslator>(provider.GetRequiredService<ResponsesStreamToChatTranslator>());
        Assert.IsType<ModelListService>(provider.GetRequiredService<ModelListService>());
        Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>());

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxSecretServiceCredentialStore>(provider.GetRequiredService<ICopilotCredentialStore>());
            Assert.IsType<CopilotCliConfigMetadataReader>(provider.GetRequiredService<CopilotCliConfigMetadataReader>());
            Assert.Equal("SecretServiceDbusClient", provider.GetRequiredService<ISecretServiceClient>().GetType().Name);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsCredentialStore>(provider.GetRequiredService<ICopilotCredentialStore>());
            Assert.Null(provider.GetService<CopilotCliConfigMetadataReader>());
            Assert.Null(provider.GetService<ISecretServiceClient>());
            return;
        }

        Assert.IsType<NoOpCopilotCredentialStore>(provider.GetRequiredService<ICopilotCredentialStore>());
        Assert.Null(provider.GetService<CopilotCliConfigMetadataReader>());
        Assert.Null(provider.GetService<ISecretServiceClient>());
    }

    [Fact]
    public void AddCopilotLlm_DoesNotRegisterHostedServicesOrWorker()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddCopilotLlm();

        Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, static descriptor =>
            descriptor.ServiceType.Name == "Worker" ||
            descriptor.ImplementationType?.Name == "Worker");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCopilotLlm();
        return services.BuildServiceProvider();
    }
}
