namespace LlmSdk.Infrastructure;

public interface ISecretServiceClient
{
    string? GetCredentialSecret(string serviceName, Func<IReadOnlyList<SecretServiceItem>, SecretServiceItem?> selector);
}
