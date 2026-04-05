namespace CopilotLlm.Infrastructure;

internal static class CopilotCredentialConstants
{
    internal const string EnvironmentVariableName = "COPILOT_TOKEN";
    internal const string WindowsTargetPrefix = "copilot-cli/https://github.com";
    internal const string SecretServiceName = "copilot-cli";
    internal const string GitHubAccountPrefix = "https://github.com:";
    internal const string SecretServiceBusName = "org.freedesktop.secrets";
    internal const string SecretServiceObjectPath = "/org/freedesktop/secrets";
    internal const string SecretServiceInterface = "org.freedesktop.Secret.Service";
    internal const string SecretItemInterface = "org.freedesktop.Secret.Item";
    internal const string PropertiesInterface = "org.freedesktop.DBus.Properties";
}
