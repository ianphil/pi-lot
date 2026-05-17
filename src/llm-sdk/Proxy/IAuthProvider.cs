namespace LlmSdk.Proxy;

/// <summary>
/// Port for loading and validating Copilot credentials.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Gets whether a credential has been loaded.
    /// </summary>
    bool IsAuthenticated { get; }
    /// <summary>
    /// Attempts to load a credential from the configured store.
    /// </summary>
    bool TryLoadCredential();
    /// <summary>
    /// Validates the currently loaded credential.
    /// </summary>
    Task<bool> ValidateTokenAsync();
}
