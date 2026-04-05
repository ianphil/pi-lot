using System.CommandLine;

namespace llm_cli.Commands;

public static class CommandOptions
{
    public static Argument<string> Prompt()
        => new("prompt") { Description = "The prompt to send" };

    public static Option<string> Model(string defaultModel)
        => new("--model", "-m")
        {
            Description = "Model to use",
            DefaultValueFactory = _ => defaultModel,
        };

    public static Option<string?> System()
        => new("--system", "-s") { Description = "System instructions" };

    public static Option<bool> NoStream()
        => new("--no-stream") { Description = "Disable streaming" };

    public static Option<bool> Tools()
        => new("--tools") { Description = "Enable local tools (currently: fetch_url)" };

    public static Option<string> Endpoint()
        => new("--endpoint", "-e")
        {
            Description = "Base URL of the LLM proxy",
            DefaultValueFactory = _ => "http://localhost:5100",
        };
}
