namespace CopilotLlm.Core.Ports;

public interface IAuthProvider
{
    bool IsAuthenticated { get; }
    bool TryLoadCredential();
    Task<bool> ValidateTokenAsync();
}
