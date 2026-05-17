namespace LlmSdk.Client;

/// <summary>
/// Configures process-wide defaults for <see cref="ILlmSdkClient"/> and related SDK services.
/// </summary>
public sealed class LlmSdkOptions
{
    /// <summary>
    /// Gets or sets the model used when a request does not specify one.
    /// </summary>
    /// <remarks>
    /// If null, callers must provide a model on each request that requires one.
    /// </remarks>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Gets or sets the timeout applied to upstream Copilot HTTP requests.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(120);
}
