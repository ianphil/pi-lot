using System.CommandLine;
using System.Text.Json;

namespace llm_cli.Commands;

public static class HealthCommand
{
    public static Command Build(Option<string> endpointOption)
    {
        var command = new Command("health", "Check if the proxy is running and authenticated")
        {
            endpointOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var endpoint = parseResult.GetValue(endpointOption)!;
            using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
            try
            {
                var response = await http.GetAsync("/health", cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);

                var status = doc.RootElement.GetProperty("status").GetString();
                var auth = doc.RootElement.GetProperty("authenticated").GetBoolean();

                Console.ForegroundColor = status == "healthy" ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.Write($"  {status}");
                Console.ResetColor();
                Console.WriteLine($"  authenticated={auth}  endpoint={endpoint}");
            }
            catch (HttpRequestException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  unreachable  {ex.Message}");
                Console.ResetColor();
            }
        });

        return command;
    }
}
