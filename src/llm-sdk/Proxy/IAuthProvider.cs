namespace LlmSdk.Proxy;

public interface IAuthProvider
{
    bool IsAuthenticated { get; }
    bool TryLoadCredential();
    Task<bool> ValidateTokenAsync();
}
