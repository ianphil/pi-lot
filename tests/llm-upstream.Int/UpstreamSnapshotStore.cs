using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace LlmUpstream.Int;

internal static class UpstreamSnapshotStore
{
    public static bool UpdateSnapshots =>
        string.Equals(Environment.GetEnvironmentVariable("LLM_UPSTREAM_UPDATE_SNAPSHOTS"), "1", StringComparison.Ordinal);

    public static async Task AssertMatchesSnapshotAsync(
        string fileName,
        UpstreamCaptureDocument document,
        ITestOutputHelper output,
        [CallerFilePath] string callerFilePath = "")
    {
        var snapshotPath = GetSnapshotPath(fileName, callerFilePath);
        var actual = UpstreamCaptureJson.Serialize(document);

        if (UpdateSnapshots)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, actual);
            output.WriteLine($"Updated upstream snapshot: {snapshotPath}");
            return;
        }

        Assert.True(
            File.Exists(snapshotPath),
            $"Missing upstream snapshot: {snapshotPath}{Environment.NewLine}" +
            "Run with LLM_UPSTREAM_UPDATE_SNAPSHOTS=1 to create it.");

        var expected = await File.ReadAllTextAsync(snapshotPath);
        Assert.Equal(expected, actual);
    }

    private static string GetSnapshotPath(string fileName, string callerFilePath)
    {
        var projectDirectory = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Could not resolve test source directory.");
        return Path.Combine(projectDirectory, "Snapshots", fileName);
    }
}
