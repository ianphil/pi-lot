namespace CopilotLlm.Client;

public sealed class CopilotLlmOptions
{
    public string? DefaultModel { get; set; }

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(120);
}
