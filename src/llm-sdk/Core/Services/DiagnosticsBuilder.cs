using System.Text.RegularExpressions;
using LlmSdk.Core.Models;

namespace LlmSdk.Core.Services;

public sealed class DiagnosticsBuilder
{
    private readonly List<DiagnosticEntry> _entries = [];

    public bool HasEntries => _entries.Count > 0;

    public void Add(
        DiagnosticSeverity severity,
        string code,
        string message,
        IReadOnlyDictionary<string, string>? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _entries.Add(new DiagnosticEntry(severity, code, message, Redact(detail)));
    }

    public Diagnostics? Build() =>
        _entries.Count == 0
            ? null
            : new Diagnostics(_entries.ToArray());

    private static IReadOnlyDictionary<string, string>? Redact(IReadOnlyDictionary<string, string>? detail)
    {
        if (detail is null || detail.Count == 0)
        {
            return null;
        }

        return detail.ToDictionary(
            static pair => pair.Key,
            static pair => DiagnosticRedactor.Redact(pair.Value),
            StringComparer.Ordinal);
    }
}

internal static partial class DiagnosticRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        value = BearerTokenRegex().Replace(value, "Bearer [redacted]");
        return SecretAssignmentRegex().Replace(value, match => $"{match.Groups["name"].Value}=[redacted]");
    }

    [GeneratedRegex("(?i)Bearer\\s+[^\\s,;]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(?<name>authorization|api[-_ ]?key|token)\\s*[:=]\\s*[^\\s,;]+")]
    private static partial Regex SecretAssignmentRegex();
}
