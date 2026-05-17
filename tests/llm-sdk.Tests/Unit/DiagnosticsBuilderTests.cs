using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class DiagnosticsBuilderTests
{
    [Fact]
    public void Build_WhenNoEntries_ReturnsNull()
    {
        var builder = new DiagnosticsBuilder();

        Assert.Null(builder.Build());
        Assert.False(builder.HasEntries);
    }

    [Fact]
    public void Add_WithSecretLikeDetails_RedactsValues()
    {
        var builder = new DiagnosticsBuilder();

        builder.Add(
            DiagnosticSeverity.Warning,
            "hook_threw",
            "Hook failed.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["exception"] = "InvalidOperationException: Authorization: Bearer fake-secret-token",
                ["header"] = "api-key=abc123",
            });

        var diagnostics = builder.Build();
        Assert.NotNull(diagnostics);
        var entry = Assert.Single(diagnostics.Entries);
        Assert.Contains("[redacted]", entry.Detail?["exception"], StringComparison.Ordinal);
        Assert.DoesNotContain("fake-secret-token", entry.Detail?["exception"], StringComparison.Ordinal);
        Assert.Equal("api-key=[redacted]", Assert.Contains("header", entry.Detail!));
    }

    [Fact]
    public void Diagnostics_WithEquivalentDetails_AreEqual()
    {
        var left = new Diagnostics(
        [
            new DiagnosticEntry(
                DiagnosticSeverity.Warning,
                "thinking_clamped",
                "Clamped.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["requested"] = "XHigh",
                    ["effective"] = "Medium",
                }),
        ]);
        var right = new Diagnostics(
        [
            new DiagnosticEntry(
                DiagnosticSeverity.Warning,
                "thinking_clamped",
                "Clamped.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effective"] = "Medium",
                    ["requested"] = "XHigh",
                }),
        ]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
