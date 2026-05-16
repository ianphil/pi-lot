namespace llm_cli.Agents;

public sealed record AskRequest(
    string Prompt,
    string Model,
    string? SystemInstructions,
    bool ToolsEnabled,
    string? RequestId = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    int? TimeoutMs = null,
    int? MaxRetries = null,
    int? MaxRetryDelayMs = null);
