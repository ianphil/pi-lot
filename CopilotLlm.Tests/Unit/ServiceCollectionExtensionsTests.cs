using CopilotLlm;
using CopilotLlm.Client;
using CopilotLlm.Core.Services;
using CopilotLlm.Infrastructure;
using CopilotLlm.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCopilotLlm_RegistersLibraryServices()
    {
        using var provider = CreateProvider();

        var client = provider.GetRequiredService<CopilotClient>();
        var sdkClient = provider.GetRequiredService<ICopilotLlmClient>();

        Assert.Same(client, provider.GetRequiredService<IAuthProvider>());
        Assert.Same(client, provider.GetRequiredService<IModelProvider>());
        Assert.NotNull(sdkClient);
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
            Assert.IsType<SecretServiceDbusClient>(provider.GetRequiredService<ISecretServiceClient>());
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

    [Fact]
    public void AddCopilotLlm_WithConfiguration_RegistersConfiguredOptionsAndAppliesTimeoutToCopilotClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCopilotLlm(options =>
        {
            options.DefaultModel = "gpt-5.4-mini";
            options.HttpTimeout = TimeSpan.FromSeconds(45);
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CopilotLlmOptions>>().Value;
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CopilotClient));

        Assert.Equal("gpt-5.4-mini", options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(45), options.HttpTimeout);
        Assert.Equal(TimeSpan.FromSeconds(45), httpClient.Timeout);
    }

    [Fact]
    public void AddCopilotLlm_WithoutConfiguration_UsesDefaultOptions()
    {
        using var provider = CreateProvider();

        var options = provider.GetRequiredService<IOptions<CopilotLlmOptions>>().Value;
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CopilotClient));

        Assert.Null(options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(120), options.HttpTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), httpClient.Timeout);
    }

    [Fact]
    public void AddCopilotLlm_WithNonPositiveHttpTimeout_ThrowsArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddCopilotLlm(options =>
            options.HttpTimeout = TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void AddCopilotLlm_WithBlankDefaultModel_ThrowsArgumentException(string defaultModel)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCopilotLlm(options =>
            options.DefaultModel = defaultModel));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCopilotLlm();
        return services.BuildServiceProvider();
    }
}
