namespace LlmSvc.Infrastructure;

internal static class CopilotCredentialConstants
{
    public const string EnvironmentVariableName = "COPILOT_TOKEN";
    public const string WindowsTargetPrefix = "copilot-cli/https://github.com";
    public const string SecretServiceName = "copilot-cli";
    public const string GitHubAccountPrefix = "https://github.com:";
    public const string SecretServiceBusName = "org.freedesktop.secrets";
    public const string SecretServiceObjectPath = "/org/freedesktop/secrets";
    public const string SecretServiceInterface = "org.freedesktop.Secret.Service";
    public const string SecretItemInterface = "org.freedesktop.Secret.Item";
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";
}
