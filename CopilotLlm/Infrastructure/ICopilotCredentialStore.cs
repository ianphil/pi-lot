namespace CopilotLlm.Infrastructure;

public interface ICopilotCredentialStore
{
    string DisplayName { get; }

    string? GetCredential();
}
