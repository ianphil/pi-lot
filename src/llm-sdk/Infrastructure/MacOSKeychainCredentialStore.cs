using System.Diagnostics;
using LlmSdk.Core;
using LlmSdk.Proxy;

namespace LlmSdk.Infrastructure;

/// <summary>
/// Reads the Copilot CLI credential from the macOS login Keychain using <c>/usr/bin/security</c>.
/// The Copilot CLI stores its token as a generic password with service name <c>copilot-cli</c>
/// and account <c>https://github.com:&lt;login&gt;</c>.
/// </summary>
public sealed class MacOSKeychainCredentialStore : ICopilotCredentialStore
{
    private const string SecurityToolPath = "/usr/bin/security";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);

    private readonly CopilotCliConfigMetadataReader _metadataReader;
    private readonly ILogger<MacOSKeychainCredentialStore> _logger;

    public MacOSKeychainCredentialStore(
        CopilotCliConfigMetadataReader metadataReader,
        ILogger<MacOSKeychainCredentialStore> logger)
    {
        _metadataReader = metadataReader;
        _logger = logger;
    }

    public string DisplayName => "macOS Keychain";

    public string? GetCredential()
    {
        try
        {
            var metadata = _metadataReader.Read();
            var account = metadata.PreferredAccount;

            if (account is not null)
            {
                var byAccount = TryReadKeychain(account);
                if (!string.IsNullOrWhiteSpace(byAccount))
                {
                    return byAccount;
                }
                _logger.LogDebug("macOS Keychain lookup for account {Account} did not return a secret; falling back to first match.", account);
            }

            var firstMatch = TryReadKeychain(account: null);
            return string.IsNullOrWhiteSpace(firstMatch) ? null : firstMatch;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            _logger.LogWarning(LogEvents.CredentialMissing, ex, "macOS Keychain lookup failed.");
            return null;
        }
    }

    private string? TryReadKeychain(string? account)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = SecurityToolPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("find-generic-password");
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(CopilotCredentialConstants.SecretServiceName);
        if (account is not null)
        {
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(account);
        }
        startInfo.ArgumentList.Add("-w");

        using var process = Process.Start(startInfo)
            ?? throw new IOException($"Failed to start {SecurityToolPath}.");

        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{SecurityToolPath} did not exit within {ProcessTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        return output.TrimEnd('\r', '\n');
    }
}
