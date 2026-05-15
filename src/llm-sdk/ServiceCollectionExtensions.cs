using LlmSdk.Client;
using LlmSdk.Core.Services;
using LlmSdk.Infrastructure;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LlmSdk;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmSdk(this IServiceCollection services)
    {
        return services.AddLlmSdk(static _ => { });
    }

    public static IServiceCollection AddLlmSdk(
        this IServiceCollection services,
        Action<LlmSdkOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LlmSdkOptions();
        configure(options);
        ValidateOptions(options);

        services.AddSingleton(options);
        services.AddSingleton<IOptions<LlmSdkOptions>>(Options.Create(options));
        services.AddHttpClient(nameof(CopilotClient), client => client.Timeout = options.HttpTimeout);
        AddLibraryServices(services);

        return services;
    }

    private static void ValidateOptions(LlmSdkOptions options)
    {
        if (options.HttpTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.HttpTimeout), "HttpTimeout must be greater than zero.");
        }

        if (options.DefaultModel is not null && string.IsNullOrWhiteSpace(options.DefaultModel))
        {
            throw new ArgumentException("DefaultModel must be non-empty when provided.", nameof(options.DefaultModel));
        }
    }

    private static void AddLibraryServices(IServiceCollection services)
    {
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<CopilotCliConfigMetadataReader>();
            services.AddSingleton<ISecretServiceClient, SecretServiceDbusClient>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<CopilotCliConfigMetadataReader>();
        }

        services.AddSingleton<ICopilotCredentialStore>(static sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsCredentialStore();
            }

            if (OperatingSystem.IsLinux())
            {
                return ActivatorUtilities.CreateInstance<LinuxSecretServiceCredentialStore>(sp);
            }

            if (OperatingSystem.IsMacOS())
            {
                return ActivatorUtilities.CreateInstance<MacOSKeychainCredentialStore>(sp);
            }

            return new NoOpCopilotCredentialStore();
        });

        services.AddSingleton<CopilotClient>(sp => ActivatorUtilities.CreateInstance<CopilotClient>(
            sp,
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<CopilotClient>());
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<CopilotClient>());
        services.AddSingleton<ChatCompletionsTranslator>();
        services.AddSingleton<ChatCompletionsStreamTranslator>();
        services.AddSingleton<ModelListService>();
        services.AddSingleton<ILlmSdkClient, LlmSdkClient>();
        services.AddSingleton<IResponsesService, ResponsesService>();
        services.AddSingleton<ResponsesStreamToChatTranslator>();
        services.AddSingleton<IChatCompletionsService, ChatCompletionsService>();
    }
}
