namespace LlmSdk.Infrastructure;

public sealed record CopilotCliLoginMetadata(string? LastLoggedInUser, string[] LoggedInUsers, string ConfigPath)
{
    public string? PreferredAccount =>
        string.IsNullOrWhiteSpace(LastLoggedInUser)
            ? null
            : $"{CopilotCredentialConstants.GitHubAccountPrefix}{LastLoggedInUser}";
}
