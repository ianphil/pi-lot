using CopilotLlm.Core.Ports;

namespace CopilotLlm.Infrastructure;

public sealed class NoOpCopilotCredentialStore : ICopilotCredentialStore
{
    public string DisplayName => "unsupported platform secure store";

    public string? GetCredential() => null;
}
