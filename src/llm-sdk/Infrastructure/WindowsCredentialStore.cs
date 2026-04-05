using LlmSdk.Proxy;

namespace LlmSdk.Infrastructure;

public sealed class WindowsCredentialStore : ICopilotCredentialStore
{
    public string DisplayName => "Windows Credential Manager";

    public string? GetCredential() => CredentialManager.GetCredential(CopilotCredentialConstants.WindowsTargetPrefix);
}
