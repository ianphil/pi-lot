using System.Text;
using System.Text.Json;

namespace LlmSdk.Core.Services;

public static class PartialJsonParser
{
    public static JsonElement? TryParse(string incomplete)
    {
        ArgumentNullException.ThrowIfNull(incomplete);

        for (var length = incomplete.Length; length > 0; length--)
        {
            var candidate = Complete(incomplete.AsSpan(0, length));
            if (candidate is null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(candidate);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static string? Complete(ReadOnlySpan<char> prefix)
    {
        var builder = new StringBuilder(prefix.Length + 16);
        var closers = new List<char>();
        var inString = false;
        var escaping = false;

        foreach (var character in prefix)
        {
            builder.Append(character);

            if (inString)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    closers.Add('}');
                    break;
                case '[':
                    closers.Add(']');
                    break;
                case '}':
                case ']':
                    if (closers.Count == 0 || closers[^1] != character)
                    {
                        return null;
                    }

                    closers.RemoveAt(closers.Count - 1);
                    break;
            }
        }

        if (inString)
        {
            builder.Append('"');
        }

        for (var index = closers.Count - 1; index >= 0; index--)
        {
            builder.Append(closers[index]);
        }

        return builder.ToString();
    }
}
