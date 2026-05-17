using System.Text.Json;
using LlmAgent;
using LlmSdk;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "Use the get_current_time tool with timezone set to utc, then tell me the time.";

var services = new ServiceCollection();
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
services.AddLlmSdk(options => options.DefaultModel = "gpt-5.4-mini");

await using var provider = services.BuildServiceProvider();
var auth = provider.GetRequiredService<IAuthProvider>();
if (!auth.TryLoadCredential())
{
    Console.Error.WriteLine("Could not load Copilot credentials. Set COPILOT_TOKEN or sign in with Copilot CLI.");
    return 1;
}

var client = provider.GetRequiredService<ILlmSdkClient>();
var options = new AgentLoopOptions
{
    Model = "gpt-5.4-mini",
    Instructions = "You are a concise assistant. Use tools when they are relevant.",
    Tools = [new CurrentTimeTool()],
    MaxTurns = 4,
    PromptCacheKey = $"simple-agent-example-{Environment.UserName}",
    TimeoutMs = 60000,
    MaxRetries = 1,
    MaxRetryDelayMs = 1000,
};

Console.WriteLine($"> {prompt}");
Console.WriteLine();

await foreach (var evt in AgentLoop.RunAsync(client, prompt, options))
{
    switch (evt)
    {
        case MessageDelta { StreamEvent: TextDelta delta }:
            Console.Write(delta.Text);
            break;

        case ToolExecutionStarted(_, var toolName, var arguments):
            Console.WriteLine();
            Console.WriteLine($"[tool:start] {toolName} {arguments}");
            break;

        case ToolExecutionEnded(_, var toolName, var result):
            Console.WriteLine($"[tool:end] {toolName}: {result.Content}");
            break;

        case AgentEnded:
            Console.WriteLine();
            break;
    }
}

return 0;

internal sealed class CurrentTimeTool : IAgentTool
{
    public string Name => "get_current_time";

    public string Description => "Get the current time for a supported timezone.";

    public JsonElement? Parameters { get; } = JsonSerializer.SerializeToElement(
        new
        {
            type = "object",
            properties = new
            {
                timezone = new
                {
                    type = "string",
                    @enum = new[] { "utc", "local" },
                },
            },
            required = new[] { "timezone" },
            additionalProperties = false,
        },
        JsonDefaults.Web);

    public bool? Strict => true;

    public Task<AgentToolResult> ExecuteAsync(
        string callId,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var timezone = arguments.GetProperty("timezone").GetString();
        var now = string.Equals(timezone, "local", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.Now
            : DateTimeOffset.UtcNow;

        return Task.FromResult(new AgentToolResult(now.ToString("O")));
    }
}
