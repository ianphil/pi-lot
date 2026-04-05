using System.Text;

namespace LlmSdk.Core.Services;

internal static class SseChunkParser
{
    public static ParsedSseChunk? Parse(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return null;
        }

        using var reader = new StringReader(chunk);
        string? eventName = null;
        var data = new StringBuilder();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line[5..].TrimStart());
            }
        }

        if (eventName is null && data.Length == 0)
        {
            return null;
        }

        return new ParsedSseChunk(eventName, data.ToString());
    }
}

internal readonly record struct ParsedSseChunk(string? EventName, string Data);
