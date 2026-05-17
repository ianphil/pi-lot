using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

public sealed record Diagnostics(
    [property: JsonPropertyName("entries")] IReadOnlyList<DiagnosticEntry> Entries)
{
    public bool Equals(Diagnostics? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => StructuralHash.GetSequenceHash(Entries);
}

public sealed record DiagnosticEntry(
    [property: JsonPropertyName("severity")] DiagnosticSeverity Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? Detail = null)
{
    public bool Equals(DiagnosticEntry? other) =>
        other is not null &&
        Severity == other.Severity &&
        string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        DetailEquals(Detail, other.Detail);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Severity);
        hash.Add(Code, StringComparer.Ordinal);
        hash.Add(Message, StringComparer.Ordinal);
        if (Detail is not null)
        {
            foreach (var pair in Detail.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static bool DetailEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count &&
               left.All(pair =>
                   right.TryGetValue(pair.Key, out var rightValue) &&
                   string.Equals(pair.Value, rightValue, StringComparison.Ordinal));
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
