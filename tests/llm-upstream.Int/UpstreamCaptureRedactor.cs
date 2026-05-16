using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace LlmUpstream.Int;

internal static class UpstreamCaptureRedactor
{
    public const string RedactedValue = "<REDACTED>";

    private static readonly string[] SecretHeaderFragments =
    [
        "authorization",
        "cookie",
        "secret",
        "token",
        "api-key",
        "apikey",
        "key",
    ];

    private static readonly string[] SecretJsonPropertyNames =
    [
        "authorization",
        "access_token",
        "api_key",
        "apikey",
        "cookie",
        "secret",
        "token",
    ];

    public static SortedDictionary<string, string[]> RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var redacted = new SortedDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            redacted[header.Key] = IsSecretName(header.Key)
                ? [RedactedValue]
                : header.Value.ToArray();
        }

        return redacted;
    }

    public static JsonNode? RedactJsonBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(body);
            RedactNode(node);
            return node;
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }

    public static string RedactSseRaw(string raw)
    {
        var builder = new StringBuilder();
        using var reader = new StringReader(raw);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var dataStart = "data:".Length;
                while (dataStart < line.Length && char.IsWhiteSpace(line[dataStart]))
                {
                    dataStart++;
                }

                builder
                    .Append(line.AsSpan(0, dataStart))
                    .Append(RedactJsonForRaw(line[dataStart..]))
                    .Append('\n');
            }
            else
            {
                builder.Append(line).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string RedactJsonForRaw(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var changed = RedactNode(node);
            return changed && node is not null
                ? node.ToJsonString(UpstreamCaptureJson.CompactOptions)
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static bool RedactNode(JsonNode? node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    if (IsSecretJsonProperty(property.Key))
                    {
                        obj[property.Key] = RedactedValue;
                        changed = true;
                    }
                    else
                    {
                        changed |= RedactNode(property.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    changed |= RedactNode(item);
                }

                break;
        }

        return changed;
    }

    private static bool IsSecretName(string name) =>
        SecretHeaderFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsSecretJsonProperty(string name) =>
        SecretJsonPropertyNames.Contains(name, StringComparer.OrdinalIgnoreCase);
}
