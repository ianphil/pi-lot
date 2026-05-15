using System.Diagnostics;
using LlmSdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MacOSKeychainCredentialStoreTests
{
    [Fact]
    public void DisplayName_IsHumanReadable()
    {
        var store = CreateStore();
        Assert.Equal("macOS Keychain", store.DisplayName);
    }

    [Fact]
    public void GetCredential_OnNonMacOS_ReturnsNull()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = CreateStore();
        Assert.Null(store.GetCredential());
    }

    [Fact]
    public void GetCredential_OnMacOS_WhenKeychainHasCopilotCliEntry_ReturnsToken()
    {
        if (!OperatingSystem.IsMacOS() || !KeychainHasCopilotCliEntry())
        {
            return;
        }

        var store = CreateStore();
        var token = store.GetCredential();

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    private static MacOSKeychainCredentialStore CreateStore()
    {
        return new MacOSKeychainCredentialStore(
            new CopilotCliConfigMetadataReader(),
            NullLogger<MacOSKeychainCredentialStore>.Instance);
    }

    private static bool KeychainHasCopilotCliEntry()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                ArgumentList = { "find-generic-password", "-s", "copilot-cli" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null || !process.WaitForExit(5000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
