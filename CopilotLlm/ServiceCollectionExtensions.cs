using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using CopilotLlm.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotLlm;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCopilotLlm(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();

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
        services.AddSingleton<IResponsesService, ResponsesService>();
        services.AddSingleton<ResponsesStreamToChatTranslator>();
        services.AddSingleton<IChatCompletionsService, ChatCompletionsService>();

        return services;
    }
}
