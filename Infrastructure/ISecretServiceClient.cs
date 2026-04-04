namespace LlmSvc.Infrastructure;

public interface ISecretServiceClient
{
    string? GetCredentialSecret(string serviceName, Func<IReadOnlyList<SecretServiceItem>, SecretServiceItem?> selector);
}
