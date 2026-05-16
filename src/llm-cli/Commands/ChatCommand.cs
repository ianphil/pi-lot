#pragma warning disable OPENAI001

using System.ClientModel;
using System.ClientModel.Primitives;
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
        var requestId = CommandOptions.RequestId();
        var correlationId = CommandOptions.CorrelationId();
        var metadata = CommandOptions.Metadata();
        var timeoutMs = CommandOptions.TimeoutMs();
        var maxRetries = CommandOptions.MaxRetries();
        var maxRetryDelayMs = CommandOptions.MaxRetryDelayMs();

        var command = new Command("chat",
            "Send a prompt via the Chat Completions API (streams by default)")
        {
            prompt, model, system, noStream, tools, endpointOption,
            requestId, correlationId, metadata, timeoutMs, maxRetries, maxRetryDelayMs,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = CommandOptions.CreateAskRequest(
                parseResult,
                prompt,
                model,
                system,
                tools,
                requestId,
                correlationId,
                metadata,
                timeoutMs,
                maxRetries,
                maxRetryDelayMs);

            var endpoint = parseResult.GetValue(endpointOption)!;
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            options.AddPolicy(new LlmProxyRequestOptionsPolicy(request), PipelinePosition.PerCall);
            var client = new ChatClient(
                parseResult.GetValue(model)!,
                new ApiKeyCredential("unused"),
                options);

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
