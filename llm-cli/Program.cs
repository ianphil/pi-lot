#pragma warning disable OPENAI001

using System.ClientModel;
using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using OpenAI;
using OpenAI.Responses;

static string LoadHelpText()
{
    var asm = Assembly.GetExecutingAssembly();
    using var stream = asm.GetManifestResourceStream("llm_cli.help.txt")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

var endpointOption = new Option<string>("--endpoint", "-e")
{
    Description = "Base URL of the LLM proxy",
    DefaultValueFactory = _ => "http://localhost:5100",
};

var root = new RootCommand(LoadHelpText());

// ── llm ask ──────────────────────────────────────────────────────────────────

var askPrompt = new Argument<string>("prompt") { Description = "The prompt to send" };
var askModel = new Option<string>("--model", "-m")
{
    Description = "Model to use",
    DefaultValueFactory = _ => "gpt-5.4-mini",
};
var askSystem = new Option<string?>("--system", "-s")
{
    Description = "System instructions",
};
var askNoStream = new Option<bool>("--no-stream")
{
    Description = "Disable streaming",
};

var askCommand = new Command("ask", "Send a prompt to a language model and print the response (streams by default)")
{
    askPrompt, askModel, askSystem, askNoStream, endpointOption,
};

askCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var prompt = parseResult.GetValue(askPrompt)!;
    var model = parseResult.GetValue(askModel)!;
    var system = parseResult.GetValue(askSystem);
    var noStream = parseResult.GetValue(askNoStream);
    var endpoint = parseResult.GetValue(endpointOption)!;

    var client = new ResponsesClient(
        new ApiKeyCredential("unused"),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    if (noStream)
    {
        var options = new CreateResponseOptions { Model = model };
        if (system is not null) options.Instructions = system;
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

        ResponseResult result = await client.CreateResponseAsync(options, cancellationToken);
        Console.WriteLine(result.GetOutputText());
    }
    else
    {
        var options = new CreateResponseOptions { Model = model, StreamingEnabled = true };
        if (system is not null) options.Instructions = system;
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

        AsyncCollectionResult<StreamingResponseUpdate> updates =
            client.CreateResponseStreamingAsync(options, cancellationToken);

        await foreach (var update in updates)
        {
            if (update is StreamingResponseOutputTextDeltaUpdate delta)
                Console.Write(delta.Delta);
        }
        Console.WriteLine();
    }
});

root.Subcommands.Add(askCommand);

// ── llm models ───────────────────────────────────────────────────────────────

var modelsCommand = new Command("models", "List available models with their supported endpoints") { endpointOption };

modelsCommand.SetAction(async (parseResult, cancellationToken) =>
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

root.Subcommands.Add(modelsCommand);

// ── llm health ───────────────────────────────────────────────────────────────

var healthCommand = new Command("health", "Check if the proxy is running and authenticated") { endpointOption };

healthCommand.SetAction(async (parseResult, cancellationToken) =>
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

root.Subcommands.Add(healthCommand);

var parseResult = root.Parse(args);
return await parseResult.InvokeAsync();
