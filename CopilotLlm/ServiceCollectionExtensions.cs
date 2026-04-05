using CopilotLlm.Client;
using CopilotLlm.Core.Services;
using CopilotLlm.Infrastructure;
using CopilotLlm.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CopilotLlm;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCopilotLlm(this IServiceCollection services)
    {
        return services.AddCopilotLlm(static _ => { });
    }

    public static IServiceCollection AddCopilotLlm(
        this IServiceCollection services,
        Action<CopilotLlmOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CopilotLlmOptions();
        configure(options);
        ValidateOptions(options);

        services.AddSingleton(options);
        services.AddSingleton<IOptions<CopilotLlmOptions>>(Options.Create(options));
        services.AddHttpClient(nameof(CopilotClient), client => client.Timeout = options.HttpTimeout);
        AddLibraryServices(services);

        return services;
    }

    private static void ValidateOptions(CopilotLlmOptions options)
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
        services.AddSingleton<CopilotLlmClient>();
        services.AddSingleton<IResponsesService, ResponsesService>();
        services.AddSingleton<ResponsesStreamToChatTranslator>();
        services.AddSingleton<IChatCompletionsService, ChatCompletionsService>();
    }
}
