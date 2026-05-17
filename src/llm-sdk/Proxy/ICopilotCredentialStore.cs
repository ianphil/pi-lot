namespace LlmSdk.Proxy;

/// <summary>
/// Stores and retrieves Copilot credentials for an operating-system credential backend.
/// </summary>
public interface ICopilotCredentialStore
{
    /// <summary>
    /// Human-readable credential store name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the stored credential, or null when no credential is available.
    /// </summary>
    string? GetCredential();
}
