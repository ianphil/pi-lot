using System.CommandLine;
using llm_cli.Agents;
using LlmSdk;
using LlmSdk.Client;
using Microsoft.Extensions.DependencyInjection;

namespace llm_cli.Commands;

public static class SdkAskCommand
{
    public static Command Build()
    {
        var prompt = CommandOptions.Prompt();
        var model = CommandOptions.Model("gpt-5.4-mini");
        var system = CommandOptions.System();
        var noStream = CommandOptions.NoStream();

        var command = new Command("sdk-ask",
            "Send a prompt directly through the LlmSdk Responses client (streams by default)")
        {
            prompt, model, system, noStream,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var modelValue = parseResult.GetValue(model)!;
            var request = new AskRequest(
                parseResult.GetValue(prompt)!,
                modelValue,
                parseResult.GetValue(system),
                false);

            return await RunSdkCommandAsync(modelValue, async client =>
            {
                if (parseResult.GetValue(noStream))
                {
                    await SdkAskAgent.RunNonStreamingAsync(client, request, Console.Out, cancellationToken);
                }
                else
                {
                    await SdkAskAgent.RunStreamingAsync(client, request, Console.Out, cancellationToken);
                }
            });
        });

        return command;
    }

    internal static async Task<int> RunSdkCommandAsync(
        string defaultModel,
        Func<ILlmSdkClient, Task> executeAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModel);
        ArgumentNullException.ThrowIfNull(executeAsync);

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLlmSdk(options => options.DefaultModel = defaultModel);

            using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<ILlmSdkClient>();
            await executeAsync(client);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
