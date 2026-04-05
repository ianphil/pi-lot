#pragma warning disable OPENAI001

using System.ClientModel;
using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using LlmSdk;
using LlmSdk.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using llm_cli;

static string LoadHelpText()
{
    var asm = Assembly.GetExecutingAssembly();
    using var stream = asm.GetManifestResourceStream("llm_cli.help.txt")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static async Task<int> RunSdkCommandAsync(
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
var askTools = new Option<bool>("--tools")
{
    Description = "Enable local tools (currently: fetch_url)",
};

var askCommand = new Command("ask", "Send a prompt to a language model and print the response (streams by default)")
{
    askPrompt, askModel, askSystem, askNoStream, askTools, endpointOption,
};

askCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var prompt = parseResult.GetValue(askPrompt)!;
    var model = parseResult.GetValue(askModel)!;
    var system = parseResult.GetValue(askSystem);
    var noStream = parseResult.GetValue(askNoStream);
    var toolsEnabled = parseResult.GetValue(askTools);
    var endpoint = parseResult.GetValue(endpointOption)!;

    var client = new ResponsesClient(
        new ApiKeyCredential("unused"),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    using var toolHttpClient = new HttpClient();
    var toolRegistry = LocalToolRegistry.CreateDefault(toolHttpClient);
    var askAgent = AskAgent.Create(client, toolRegistry, Console.Out);
    var request = new AskRequest(prompt, model, system, toolsEnabled);

    if (noStream)
    {
        Console.WriteLine(await askAgent.RunNonStreamingAsync(request, cancellationToken));
    }
    else
    {
        await askAgent.RunStreamingAsync(request, cancellationToken);
    }
});

root.Subcommands.Add(askCommand);

// ── llm chat ─────────────────────────────────────────────────────────────────

var chatPrompt = new Argument<string>("prompt") { Description = "The prompt to send" };
var chatModel = new Option<string>("--model", "-m")
{
    Description = "Model to use",
    DefaultValueFactory = _ => "gpt-5-mini",
};
var chatSystem = new Option<string?>("--system", "-s")
{
    Description = "System instructions",
};
var chatNoStream = new Option<bool>("--no-stream")
{
    Description = "Disable streaming",
};
var chatTools = new Option<bool>("--tools")
{
    Description = "Enable local tools (currently: fetch_url)",
};

var chatCommand = new Command("chat", "Send a prompt via the Chat Completions API (streams by default)")
{
    chatPrompt, chatModel, chatSystem, chatNoStream, chatTools, endpointOption,
};

chatCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var prompt = parseResult.GetValue(chatPrompt)!;
    var model = parseResult.GetValue(chatModel)!;
    var system = parseResult.GetValue(chatSystem);
    var noStream = parseResult.GetValue(chatNoStream);
    var toolsEnabled = parseResult.GetValue(chatTools);
    var endpoint = parseResult.GetValue(endpointOption)!;

    var client = new ChatClient(
        model,
        new ApiKeyCredential("unused"),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    using var toolHttpClient = new HttpClient();
    var toolRegistry = LocalToolRegistry.CreateDefault(toolHttpClient);
    var chatAgent = ChatAgent.Create(client, toolRegistry, Console.Out);
    var request = new AskRequest(prompt, model, system, toolsEnabled);

    if (noStream)
    {
        Console.WriteLine(await chatAgent.RunNonStreamingAsync(request, cancellationToken));
    }
    else
    {
        await chatAgent.RunStreamingAsync(request, cancellationToken);
    }
});

root.Subcommands.Add(chatCommand);

// ── llm sdk-ask ───────────────────────────────────────────────────────────────

var sdkAskPrompt = new Argument<string>("prompt") { Description = "The prompt to send" };
var sdkAskModel = new Option<string>("--model", "-m")
{
    Description = "Model to use",
    DefaultValueFactory = _ => "gpt-5.4-mini",
};
var sdkAskSystem = new Option<string?>("--system", "-s")
{
    Description = "System instructions",
};
var sdkAskNoStream = new Option<bool>("--no-stream")
{
    Description = "Disable streaming",
};

var sdkAskCommand = new Command("sdk-ask", "Send a prompt directly through the LlmSdk Responses client (streams by default)")
{
    sdkAskPrompt, sdkAskModel, sdkAskSystem, sdkAskNoStream,
};

sdkAskCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var prompt = parseResult.GetValue(sdkAskPrompt)!;
    var model = parseResult.GetValue(sdkAskModel)!;
    var system = parseResult.GetValue(sdkAskSystem);
    var noStream = parseResult.GetValue(sdkAskNoStream);
    var request = new AskRequest(prompt, model, system, false);

    return await RunSdkCommandAsync(model, async client =>
    {
        if (noStream)
        {
            await SdkAskAgent.RunNonStreamingAsync(client, request, Console.Out, cancellationToken);
        }
        else
        {
            await SdkAskAgent.RunStreamingAsync(client, request, Console.Out, cancellationToken);
        }
    });
});

root.Subcommands.Add(sdkAskCommand);

// ── llm sdk-chat ──────────────────────────────────────────────────────────────

var sdkChatPrompt = new Argument<string>("prompt") { Description = "The prompt to send" };
var sdkChatModel = new Option<string>("--model", "-m")
{
    Description = "Model to use",
    DefaultValueFactory = _ => "gpt-5-mini",
};
var sdkChatSystem = new Option<string?>("--system", "-s")
{
    Description = "System instructions",
};
var sdkChatNoStream = new Option<bool>("--no-stream")
{
    Description = "Disable streaming",
};

var sdkChatCommand = new Command("sdk-chat", "Send a prompt directly through the LlmSdk Chat Completions client (streams by default)")
{
    sdkChatPrompt, sdkChatModel, sdkChatSystem, sdkChatNoStream,
};

sdkChatCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var prompt = parseResult.GetValue(sdkChatPrompt)!;
    var model = parseResult.GetValue(sdkChatModel)!;
    var system = parseResult.GetValue(sdkChatSystem);
    var noStream = parseResult.GetValue(sdkChatNoStream);
    var request = new AskRequest(prompt, model, system, false);

    return await RunSdkCommandAsync(model, async client =>
    {
        if (noStream)
        {
            await SdkChatAgent.RunNonStreamingAsync(client, request, Console.Out, cancellationToken);
        }
        else
        {
            await SdkChatAgent.RunStreamingAsync(client, request, Console.Out, cancellationToken);
        }
    });
});

root.Subcommands.Add(sdkChatCommand);

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

