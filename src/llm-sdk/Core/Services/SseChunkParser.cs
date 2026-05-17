using System.Text;

namespace LlmSdk.Core.Services;

internal sealed class SseChunkParser
{
    private readonly StringBuilder _buffer = new();
    private string? _pendingData;
    private string? _pendingEventName;

    public static ParsedSseChunk? Parse(string chunk) => new SseChunkParser().ParseChunk(chunk, requireTerminator: false);

    public ParsedSseChunk? ParseChunk(string chunk) => ParseChunk(chunk, requireTerminator: true);

    private ParsedSseChunk? ParseChunk(string chunk, bool requireTerminator)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return null;
        }

        _buffer.Append(chunk);
        var buffered = _buffer.ToString();
        var eventEnd = buffered.IndexOf("\n\n", StringComparison.Ordinal);
        var terminatorLength = 2;
        if (eventEnd < 0)
        {
            eventEnd = buffered.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            terminatorLength = 4;
        }

        if (eventEnd < 0)
        {
            if (requireTerminator)
            {
                return null;
            }

            eventEnd = _buffer.Length;
            terminatorLength = 0;
        }

        var eventChunk = _buffer.ToString(0, eventEnd);
        _buffer.Remove(0, eventEnd + terminatorLength);

        using var reader = new StringReader(eventChunk);
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

        eventName = _pendingEventName ?? eventName;
        var parsedData = (_pendingData ?? string.Empty) + data;
        _pendingEventName = null;
        _pendingData = null;

        if (parsedData.Length > 0 && char.IsHighSurrogate(parsedData[^1]))
        {
            _pendingEventName = eventName;
            _pendingData = parsedData;
            return null;
        }

        return new ParsedSseChunk(eventName, RemoveInvalidSurrogates(parsedData));
    }

    private static string RemoveInvalidSurrogates(string value)
    {
        var sanitized = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sanitized.Append(current).Append(value[++i]);
                }

                continue;
            }

            if (!char.IsLowSurrogate(current))
            {
                sanitized.Append(current);
            }
        }

        return sanitized.ToString();
    }
}

internal readonly record struct ParsedSseChunk(string? EventName, string Data);
