using LlmSdk.Infrastructure;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CopilotCliConfigMetadataReaderTests : IDisposable
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
    public void Read_WhenConfigFileIsMissing_ReturnsNoPreference()
    {
        var configPath = Path.Combine(CreateTempDirectory(), "config.json");
        var reader = new CopilotCliConfigMetadataReader(configPath);

        var metadata = reader.Read();

        Assert.Null(metadata.LastLoggedInUser);
        Assert.Empty(metadata.LoggedInUsers);
        Assert.Equal(configPath, metadata.ConfigPath);
    }

    [Fact]
    public void Read_WhenConfigFileIsMalformed_ReturnsNoPreference()
    {
        var tempDirectory = CreateTempDirectory();
        var configPath = Path.Combine(tempDirectory, "config.json");
        File.WriteAllText(configPath, "{ not-json");

        var reader = new CopilotCliConfigMetadataReader(configPath);
        var metadata = reader.Read();

        Assert.Null(metadata.LastLoggedInUser);
        Assert.Empty(metadata.LoggedInUsers);
    }

    [Fact]
    public void Read_WhenConfigFileUsesLegacyShape_ReturnsLastLoggedInUserAndKnownUsers()
    {
        var tempDirectory = CreateTempDirectory();
        var configPath = Path.Combine(tempDirectory, "config.json");
        File.WriteAllText(
            configPath,
            """
            {
              "last_logged_in_user": "octocat",
              "logged_in_users": {
                "hubot": {},
                "octocat": {}
              }
            }
            """);

        var reader = new CopilotCliConfigMetadataReader(configPath);
        var metadata = reader.Read();

        Assert.Equal("octocat", metadata.LastLoggedInUser);
        Assert.Equal(["hubot", "octocat"], metadata.LoggedInUsers);
        Assert.Equal("https://github.com:octocat", metadata.PreferredAccount);
    }

    [Fact]
    public void Read_WhenConfigFileUsesCurrentCopilotShape_ReturnsLastLoggedInUserAndKnownUsers()
    {
        var tempDirectory = CreateTempDirectory();
        var configPath = Path.Combine(tempDirectory, "config.json");
        File.WriteAllText(
            configPath,
            """
            {
              "last_logged_in_user": {
                "host": "https://github.com",
                "login": "octocat"
              },
              "logged_in_users": [
                {
                  "host": "https://github.com",
                  "login": "hubot"
                },
                {
                  "host": "https://github.com",
                  "login": "octocat"
                },
                {
                  "host": "https://example.com",
                  "login": "ignored"
                }
              ]
            }
            """);

        var reader = new CopilotCliConfigMetadataReader(configPath);
        var metadata = reader.Read();

        Assert.Equal("octocat", metadata.LastLoggedInUser);
        Assert.Equal(["hubot", "octocat"], metadata.LoggedInUsers);
        Assert.Equal("https://github.com:octocat", metadata.PreferredAccount);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
