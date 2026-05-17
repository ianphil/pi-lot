using System.Text;

namespace LlmSdk.Proxy;

/// <summary>
/// HTTP-shaped result returned by SDK ports for body and stream responses.
/// </summary>
public sealed class ResponseHttpResult
{
    private ResponseHttpResult(
        string? body,
        IAsyncEnumerable<string>? chunks,
        int statusCode,
        string contentType,
        IReadOnlyDictionary<string, string[]>? headers)
    {
        Body = body;
        Chunks = chunks;
        StatusCode = statusCode;
        ContentType = contentType;
        Headers = headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Response body when this is a buffered result.
    /// </summary>
    public string? Body { get; }

    /// <summary>
    /// Response chunks when this is a streaming result.
    /// </summary>
    public IAsyncEnumerable<string>? Chunks { get; }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Response content type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Response headers keyed case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; }

    /// <summary>
    /// Creates a buffered HTTP-shaped result.
    /// </summary>
    public static ResponseHttpResult FromBody(
        string body,
        int statusCode,
        string contentType,
        IReadOnlyDictionary<string, string[]>? headers = null) =>
        new(body, null, statusCode, contentType, headers);

    /// <summary>
    /// Creates a streaming HTTP-shaped result.
    /// </summary>
    public static ResponseHttpResult FromStream(
        IAsyncEnumerable<string> chunks,
        int statusCode = 200,
        string contentType = "text/event-stream",
        IReadOnlyDictionary<string, string[]>? headers = null) =>
        new(null, chunks, statusCode, contentType, headers);

    /// <summary>
    /// Reads the buffered body or drains stream chunks into a single string.
    /// </summary>
    public async Task<string> ReadBodyAsync(CancellationToken cancellationToken = default)
    {
        if (Body is not null)
        {
            return Body;
        }

        if (Chunks is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        await foreach (var chunk in Chunks.WithCancellation(cancellationToken))
        {
            builder.Append(chunk);
        }

        return builder.ToString();
    }
}
