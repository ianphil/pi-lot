namespace LlmSdk.Client;

public sealed class LlmSdkOptions
{
    public string? DefaultModel { get; set; }

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(120);
}
