namespace llm_cli.Agents;

public sealed record AskRequest(
    string Prompt,
    string Model,
    string? SystemInstructions,
    bool ToolsEnabled);
