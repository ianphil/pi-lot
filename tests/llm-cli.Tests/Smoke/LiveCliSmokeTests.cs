using System.Diagnostics;
using System.Text.RegularExpressions;

namespace llm_cli.Tests.Smoke;

/// <summary>
/// Live smoke tests that exercise the llm CLI against a running instance at localhost:5100.
/// Run with: dotnet test llm-cli.Tests --filter Category=Smoke
/// Requires the service to be running with valid Copilot credentials and internet access.
/// </summary>
[Trait("Category", "Smoke")]
public sealed partial class LiveCliSmokeTests : IDisposable
{
    private const string ReadmeUrl = "https://raw.githubusercontent.com/dotnet/runtime/main/README.md";

    private readonly HttpClient _httpClient;

    public LiveCliSmokeTests()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task Ask_WithTools_FetchesReadmeAndReturnsFirstHeading()
    {
        var expectedHeading = NormalizeHeading(await GetFirstHeadingAsync());

        var result = await RunCliAsync(
            "ask",
            $"Fetch {ReadmeUrl} and answer with only the first markdown heading from that document, without the leading # symbol.",
            "--tools",
            "--no-stream",
            "-m",
            "gpt-5.4-mini",
            "-e",
            "http://localhost:5100");

        Assert.True(
            result.ExitCode == 0,
            $"CLI exited with code {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");

        var actualHeading = NormalizeHeading(result.StandardOutput);
        Assert.Equal(expectedHeading, actualHeading);
    }

    private async Task<string> GetFirstHeadingAsync()
    {
        var markdown = await _httpClient.GetStringAsync(ReadmeUrl);
        var firstHeading = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .First(line => line.StartsWith("# ", StringComparison.Ordinal));

        return firstHeading[2..].Trim();
    }

    private static async Task<CliProcessResult> RunCliAsync(params string[] args)
    {
        var cliDllPath = Path.Combine(AppContext.BaseDirectory, "llm.dll");
        Assert.True(File.Exists(cliDllPath), $"Could not find llm CLI assembly at '{cliDllPath}'.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add(cliDllPath);

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start llm CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CliProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string NormalizeHeading(string value)
    {
        var normalized = value.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            return normalized;
        }

        normalized = normalized
            .Trim('`', '"', '\'', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var firstLine = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? string.Empty;

        if (firstLine.StartsWith("#", StringComparison.Ordinal))
        {
            firstLine = firstLine.TrimStart('#').Trim();
        }

        return WhitespaceRegex().Replace(firstLine, " ");
    }

    private sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
