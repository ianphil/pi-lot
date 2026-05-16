#pragma warning disable OPENAI001

using System.ClientModel;
using System.ClientModel.Primitives;
using System.CommandLine;
using llm_cli.Agents;
using OpenAI;
using OpenAI.Responses;

namespace llm_cli.Commands;

public static class AskCommand
{
    public static Command Build(Option<string> endpointOption)
    {
        var prompt = CommandOptions.Prompt();
        var model = CommandOptions.Model("gpt-5.4-mini");
        var system = CommandOptions.System();
        var noStream = CommandOptions.NoStream();
        var tools = CommandOptions.Tools();
        var requestId = CommandOptions.RequestId();
        var correlationId = CommandOptions.CorrelationId();
        var metadata = CommandOptions.Metadata();
        var timeoutMs = CommandOptions.TimeoutMs();
        var maxRetries = CommandOptions.MaxRetries();
        var maxRetryDelayMs = CommandOptions.MaxRetryDelayMs();

        var command = new Command("ask",
            "Send a prompt to a language model and print the response (streams by default)")
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
            var client = new ResponsesClient(
                new ApiKeyCredential("unused"),
                options);

            using var toolHttpClient = new HttpClient();
            var toolRegistry = LocalToolRegistry.CreateDefault(toolHttpClient);
            var askAgent = AskAgent.Create(client, toolRegistry, Console.Out);

            if (parseResult.GetValue(noStream))
            {
                Console.WriteLine(await askAgent.RunNonStreamingAsync(request, cancellationToken));
            }
            else
            {
                await askAgent.RunStreamingAsync(request, cancellationToken);
            }
        });

        return command;
    }
}
