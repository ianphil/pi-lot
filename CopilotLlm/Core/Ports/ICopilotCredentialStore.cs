namespace CopilotLlm.Core.Ports;

public interface ICopilotCredentialStore
{
    string DisplayName { get; }

    string? GetCredential();
}
