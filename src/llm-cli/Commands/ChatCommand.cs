#pragma warning disable OPENAI001

using System.ClientModel;
using System.CommandLine;
using llm_cli.Agents;
using OpenAI;
using OpenAI.Chat;

namespace llm_cli.Commands;

public static class ChatCommand
{
    public static Command Build(Option<string> endpointOption)
    {
        var prompt = CommandOptions.Prompt();
        var model = CommandOptions.Model("gpt-5-mini");
        var system = CommandOptions.System();
        var noStream = CommandOptions.NoStream();
        var tools = CommandOptions.Tools();

        var command = new Command("chat",
            "Send a prompt via the Chat Completions API (streams by default)")
        {
            prompt, model, system, noStream, tools, endpointOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new AskRequest(
                parseResult.GetValue(prompt)!,
                parseResult.GetValue(model)!,
                parseResult.GetValue(system),
                parseResult.GetValue(tools));

            var endpoint = parseResult.GetValue(endpointOption)!;
            var client = new ChatClient(
                parseResult.GetValue(model)!,
                new ApiKeyCredential("unused"),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            using var toolHttpClient = new HttpClient();
            var toolRegistry = LocalToolRegistry.CreateDefault(toolHttpClient);
            var chatAgent = ChatAgent.Create(client, toolRegistry, Console.Out);

            if (parseResult.GetValue(noStream))
            {
                Console.WriteLine(await chatAgent.RunNonStreamingAsync(request, cancellationToken));
            }
            else
            {
                await chatAgent.RunStreamingAsync(request, cancellationToken);
            }
        });

        return command;
    }
}
