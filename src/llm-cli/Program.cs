using System.CommandLine;
using System.Reflection;
using llm_cli.Commands;

static string LoadHelpText()
{
    var asm = Assembly.GetExecutingAssembly();
    using var stream = asm.GetManifestResourceStream("llm_cli.help.txt")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

var endpointOption = CommandOptions.Endpoint();
var root = new RootCommand(LoadHelpText());

root.Subcommands.Add(AskCommand.Build(endpointOption));
root.Subcommands.Add(ChatCommand.Build(endpointOption));
root.Subcommands.Add(SdkAskCommand.Build());
root.Subcommands.Add(SdkChatCommand.Build());
root.Subcommands.Add(ModelsCommand.Build(endpointOption));
root.Subcommands.Add(HealthCommand.Build(endpointOption));

return await root.Parse(args).InvokeAsync();

