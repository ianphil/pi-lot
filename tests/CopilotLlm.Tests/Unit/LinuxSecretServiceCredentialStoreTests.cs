using CopilotLlm.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LinuxSecretServiceCredentialStoreTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

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
        Assert.Equal("/item/preferred", secretServiceClient.SelectedItem?.ItemPath);
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
        Assert.Equal("/item/first", secretServiceClient.SelectedItem?.ItemPath);
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

    [Fact]
    public void GetCredential_WhenLockedItemsExist_ExcludesLockedItems()
    {
        var configPath = WriteConfig("{}");
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/locked", "Locked", "https://github.com:alice", true),
                new SecretServiceItem("/item/unlocked", "Unlocked", "https://github.com:bob", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/unlocked"] = "bob-token",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Equal("bob-token", credential);
        Assert.Equal("/item/unlocked", secretServiceClient.SelectedItem?.ItemPath);
    }

    [Fact]
    public void GetCredential_WhenAccountIsNullOrWhitespace_ExcludesItem()
    {
        var configPath = WriteConfig("{}");
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/null-account", "NoAccount", null, false),
                new SecretServiceItem("/item/empty-account", "Empty", "  ", false),
                new SecretServiceItem("/item/valid", "Valid", "https://github.com:charlie", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/valid"] = "charlie-token",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Equal("charlie-token", credential);
        Assert.Equal("/item/valid", secretServiceClient.SelectedItem?.ItemPath);
    }

    [Fact]
    public void GetCredential_WhenSecretIsEmptyOrWhitespace_ReturnsNull()
    {
        var configPath = WriteConfig("{}");
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/empty", "Empty", "https://github.com:dave", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/empty"] = "  ",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Null(credential);
    }

    [Fact]
    public void GetCredential_WhenPreferredAccountNotInCandidates_FallsBackToFirst()
    {
        var configPath = WriteConfig(
            """
            {
              "last_logged_in_user": "missing",
              "logged_in_users": {
                "missing": {},
                "present": {}
              }
            }
            """);
        var secretServiceClient = new StubSecretServiceClient(
            [
                new SecretServiceItem("/item/present", "Present", "https://github.com:present", false),
            ],
            new Dictionary<string, string?>
            {
                ["/item/present"] = "present-token",
            });
        var store = CreateStore(configPath, secretServiceClient);

        var credential = store.GetCredential();

        Assert.Equal("present-token", credential);
        Assert.Equal("/item/present", secretServiceClient.SelectedItem?.ItemPath);
    }

    private static LinuxSecretServiceCredentialStore CreateStore(string configPath, ISecretServiceClient secretServiceClient) =>
        new(
            new CopilotCliConfigMetadataReader(configPath),
            secretServiceClient,
            NullLogger<LinuxSecretServiceCredentialStore>.Instance);

    private string WriteConfig(string content)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        _tempDirectories.Add(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "config.json");
        File.WriteAllText(configPath, content);
        return configPath;
    }

    private sealed class StubSecretServiceClient(
        IReadOnlyList<SecretServiceItem> items,
        IReadOnlyDictionary<string, string?> secrets) : ISecretServiceClient
    {
        public SecretServiceItem? SelectedItem { get; private set; }

        public string? GetCredentialSecret(string serviceName, Func<IReadOnlyList<SecretServiceItem>, SecretServiceItem?> selector)
        {
            var selected = selector(items);
            SelectedItem = selected;
            if (selected is null)
            {
                return null;
            }

            return secrets.TryGetValue(selected.ItemPath, out var secret) ? secret : null;
        }
    }

    private sealed class ThrowingSecretServiceClient(Exception exception) : ISecretServiceClient
    {
        public string? GetCredentialSecret(string serviceName, Func<IReadOnlyList<SecretServiceItem>, SecretServiceItem?> selector) => throw exception;
    }
}
