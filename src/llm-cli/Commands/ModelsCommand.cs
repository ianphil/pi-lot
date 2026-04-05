using System.CommandLine;
using System.Text.Json;

namespace llm_cli.Commands;

public static class ModelsCommand
{
    public static Command Build(Option<string> endpointOption)
    {
        var command = new Command("models", "List available models with their supported endpoints")
        {
            endpointOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var endpoint = parseResult.GetValue(endpointOption)!;
            using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
            var body = await http.GetStringAsync("/v1/models", cancellationToken);
            using var doc = JsonDocument.Parse(body);

            foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = m.GetProperty("id").GetString();
                var name = m.GetProperty("name").GetString();
                var endpoints = m.TryGetProperty("supported_endpoints", out var ep)
                    ? string.Join(", ", ep.EnumerateArray().Select(e => e.GetString()))
                    : "";
                Console.WriteLine($"  {id,-30} {name,-40} [{endpoints}]");
            }
        });

        return command;
    }
}
