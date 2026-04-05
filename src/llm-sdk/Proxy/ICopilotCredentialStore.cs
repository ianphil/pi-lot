namespace LlmSdk.Proxy;

public interface ICopilotCredentialStore
{
    string DisplayName { get; }

    string? GetCredential();
}
