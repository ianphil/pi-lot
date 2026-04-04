using System.Text.Json;

namespace CopilotLlm.Infrastructure;

public sealed class CopilotCliConfigMetadataReader
{
    private readonly string _configPath;

    public CopilotCliConfigMetadataReader(string? configPath = null)
    {
        _configPath = configPath ?? GetDefaultConfigPath();
    }

    public CopilotCliLoginMetadata Read()
    {
        if (!File.Exists(_configPath))
        {
            return new CopilotCliLoginMetadata(null, [], _configPath);
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new CopilotCliLoginMetadata(null, [], _configPath);
            }

            var loggedInUsers = ReadLoggedInUsers(root);
            var lastLoggedInUser = ReadLastLoggedInUser(root, loggedInUsers);

            return new CopilotCliLoginMetadata(lastLoggedInUser, loggedInUsers, _configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CopilotCliLoginMetadata(null, [], _configPath);
        }
    }

    private static string[] ReadLoggedInUsers(JsonElement root)
    {
        if (!root.TryGetProperty("logged_in_users", out var loggedInUsersElement))
        {
            return [];
        }

        return loggedInUsersElement.ValueKind switch
        {
            JsonValueKind.Object => loggedInUsersElement
                .EnumerateObject()
                .Select(static property => property.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            JsonValueKind.Array => loggedInUsersElement
                .EnumerateArray()
                .Select(ReadLoginEntry)
                .Where(static login => !string.IsNullOrWhiteSpace(login))
                .Select(static login => login!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static login => login, StringComparer.Ordinal)
                .ToArray(),
            _ => [],
        };
    }

    private static string? ReadLastLoggedInUser(JsonElement root, string[] loggedInUsers)
    {
        if (!root.TryGetProperty("last_logged_in_user", out var lastLoggedInUserElement))
        {
            return null;
        }

        var login = ReadLoginEntry(lastLoggedInUserElement);
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        if (loggedInUsers.Length > 0 && !loggedInUsers.Contains(login, StringComparer.Ordinal))
        {
            return null;
        }

        return login;
    }

    private static string? ReadLoginEntry(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Trim(),
            JsonValueKind.Object => ReadLoginEntryObject(element),
            _ => null,
        };
    }

    private static string? ReadLoginEntryObject(JsonElement element)
    {
        if (!element.TryGetProperty("login", out var loginElement) ||
            loginElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (element.TryGetProperty("host", out var hostElement) &&
            hostElement.ValueKind == JsonValueKind.String &&
            !string.Equals(hostElement.GetString(), "https://github.com", StringComparison.Ordinal))
        {
            return null;
        }

        return loginElement.GetString()?.Trim();
    }

    private static string GetDefaultConfigPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".copilot", "config.json");
    }
}
