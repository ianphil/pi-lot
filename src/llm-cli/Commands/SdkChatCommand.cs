using System.CommandLine;
using llm_cli.Agents;

namespace llm_cli.Commands;

public static class SdkChatCommand
{
    public static Command Build()
    {
        var prompt = CommandOptions.Prompt();
        var model = CommandOptions.Model("gpt-5-mini");
        var system = CommandOptions.System();
        var noStream = CommandOptions.NoStream();

        var command = new Command("sdk-chat",
            "Send a prompt directly through the LlmSdk Chat Completions client (streams by default)")
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

            return await SdkAskCommand.RunSdkCommandAsync(modelValue, async client =>
            {
                if (parseResult.GetValue(noStream))
                {
                    await SdkChatAgent.RunNonStreamingAsync(client, request, Console.Out, cancellationToken);
                }
                else
                {
                    await SdkChatAgent.RunStreamingAsync(client, request, Console.Out, cancellationToken);
                }
            });
        });

        return command;
    }
}
