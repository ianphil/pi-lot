using LlmSvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace llm_svc.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LinuxSecretServiceCredentialStoreTests
{
    [Fact]
    public void GetCredential_WhenPreferredAccountExists_UsesLastLoggedInUser()
    {
        var configPath = WriteConfig(
            """
            {
              "last_logged_in_user": "preferred",
              "logged_in_users": {
                "fallback": {},
                "preferred": {}
              }
            }
            """);
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/fallback", "Fallback", "https://github.com:fallback", false),
                new SecretServiceItem("/item/preferred", "Preferred", "https://github.com:preferred", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/fallback"] = "fallback-token",
                ["/item/preferred"] = "preferred-token",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Equal("preferred-token", credential);
        Assert.Equal("/item/preferred", secretServiceClient.SelectedItemPath);
    }

    [Fact]
    public void GetCredential_WhenNoPreferenceExists_FallsBackDeterministically()
    {
        var configPath = WriteConfig("{}");
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/second", "Second", "https://github.com:zoe", false),
                new SecretServiceItem("/item/first", "First", "https://github.com:adam", false),
                new SecretServiceItem("/item/ignored", "Ignored", "https://example.com:other", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/first"] = "adam-token",
                ["/item/second"] = "zoe-token",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Equal("adam-token", credential);
        Assert.Equal("/item/first", secretServiceClient.SelectedItemPath);
    }

    [Fact]
    public void GetCredential_WhenSessionBusIsUnavailable_ReturnsNull()
    {
        var configPath = WriteConfig("{}");
        var secretServiceClient = new ThrowingSecretServiceClient(new IOException("No session bus"));
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Null(credential);
    }

    private static LinuxSecretServiceCredentialStore CreateStore(string configPath, ISecretServiceClient secretServiceClient) =>
        new(
            new CopilotCliConfigMetadataReader(configPath),
            secretServiceClient,
            NullLogger<LinuxSecretServiceCredentialStore>.Instance);

    private static string WriteConfig(string content)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "config.json");
        File.WriteAllText(configPath, content);
        return configPath;
    }

    private sealed class StubSecretServiceClient(
        IReadOnlyList<SecretServiceItem> items,
        IReadOnlyDictionary<string, string?> secrets) : ISecretServiceClient
    {
        public string? SelectedItemPath { get; private set; }

        public IReadOnlyList<SecretServiceItem> SearchItems(string serviceName) => items;

        public string? GetSecret(string itemPath)
        {
            SelectedItemPath = itemPath;
            return secrets.TryGetValue(itemPath, out var secret) ? secret : null;
        }
    }

    private sealed class ThrowingSecretServiceClient(Exception exception) : ISecretServiceClient
    {
        public IReadOnlyList<SecretServiceItem> SearchItems(string serviceName) => throw exception;

        public string? GetSecret(string itemPath) => throw new NotSupportedException();
    }
}
