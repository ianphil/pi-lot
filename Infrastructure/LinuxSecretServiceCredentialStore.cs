using LlmSvc.Core;
using Tmds.DBus.Protocol;

namespace LlmSvc.Infrastructure;

public sealed class LinuxSecretServiceCredentialStore : ICopilotCredentialStore
{
    private readonly CopilotCliConfigMetadataReader _metadataReader;
    private readonly ISecretServiceClient _secretServiceClient;
    private readonly ILogger<LinuxSecretServiceCredentialStore> _logger;

    public LinuxSecretServiceCredentialStore(
        CopilotCliConfigMetadataReader metadataReader,
        ISecretServiceClient secretServiceClient,
        ILogger<LinuxSecretServiceCredentialStore> logger)
    {
        _metadataReader = metadataReader;
        _secretServiceClient = secretServiceClient;
        _logger = logger;
    }

    public string DisplayName => "Linux Secret Service";

    public string? GetCredential()
    {
        try
        {
            var metadata = _metadataReader.Read();
            var credential = _secretServiceClient.GetCredentialSecret(
                CopilotCredentialConstants.SecretServiceName,
                items => SelectCandidate(items, metadata));
            return string.IsNullOrWhiteSpace(credential) ? null : credential;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or UnauthorizedAccessException or TimeoutException)
        {
            _logger.LogWarning(LogEvents.CredentialMissing, ex, "Linux Secret Service lookup failed.");
            return null;
        }
    }

    private static SecretServiceItem? SelectCandidate(IReadOnlyList<SecretServiceItem> items, CopilotCliLoginMetadata metadata)
    {
        var candidates = items
            .Where(static item => !item.IsLocked)
            .Where(static item => !string.IsNullOrWhiteSpace(item.Account))
            .Where(static item => item.Account!.StartsWith(CopilotCredentialConstants.GitHubAccountPrefix, StringComparison.Ordinal))
            .OrderBy(static item => item.Account, StringComparer.Ordinal)
            .ThenBy(static item => item.Label ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static item => item.ItemPath, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var preferredAccount = metadata.PreferredAccount;
        if (preferredAccount is not null)
        {
            var preferredItem = candidates.FirstOrDefault(item =>
                string.Equals(item.Account, preferredAccount, StringComparison.Ordinal));

            if (preferredItem is not null)
            {
                return preferredItem;
            }
        }

        return candidates[0];
    }
}
