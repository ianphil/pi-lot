namespace LlmSvc.Infrastructure;

public interface ISecretServiceClient
{
    IReadOnlyList<SecretServiceItem> SearchItems(string serviceName);

    string? GetSecret(string itemPath);
}
