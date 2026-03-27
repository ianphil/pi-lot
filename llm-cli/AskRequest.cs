namespace llm_cli;

public sealed record AskRequest(
    string Prompt,
    string Model,
    string? SystemInstructions,
    bool ToolsEnabled);
