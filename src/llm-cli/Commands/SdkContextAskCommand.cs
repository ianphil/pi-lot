using System.CommandLine;
using llm_cli.Agents;
using LlmSdk.Core.Models;

namespace llm_cli.Commands;

public static class SdkContextAskCommand
{
    public static Command Build()
    {
        var prompt = CommandOptions.Prompt();
        var model = CommandOptions.Model("gpt-5.4-mini");
        var system = CommandOptions.System();
        var api = new Option<string>("--api")
        {
            Description = "Preferred SDK API for Context translation: responses or chat",
            DefaultValueFactory = _ => "responses",
        };

        var command = new Command("sdk-context-ask",
            "Send a prompt directly through the portable LlmSdk Context API")
        {
            prompt, model, system, api,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var modelValue = parseResult.GetValue(model)!;
            var preferredApi = ParseApi(parseResult.GetValue(api));
            var request = new AskRequest(
                parseResult.GetValue(prompt)!,
                modelValue,
                parseResult.GetValue(system),
                false);

            return await SdkAskCommand.RunSdkCommandAsync(modelValue, async client =>
            {
                await SdkContextAskAgent.RunAsync(client, request, preferredApi, Console.Out, cancellationToken);
            });
        });

        return command;
    }

    private static CompletionApi ParseApi(string? value) => value?.ToLowerInvariant() switch
    {
        "responses" => CompletionApi.Responses,
        "chat" or "chat-completions" or "chatcompletions" => CompletionApi.ChatCompletions,
        _ => throw new ArgumentException("--api must be either 'responses' or 'chat'."),
    };
}
