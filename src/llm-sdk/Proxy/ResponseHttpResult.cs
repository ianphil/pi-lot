using System.Text;

namespace LlmSdk.Proxy;

public sealed class ResponseHttpResult
{
    private ResponseHttpResult(string? body, IAsyncEnumerable<string>? chunks, int statusCode, string contentType)
    {
        Body = body;
        Chunks = chunks;
        StatusCode = statusCode;
        ContentType = contentType;
    }

    public string? Body { get; }

    public IAsyncEnumerable<string>? Chunks { get; }

    public int StatusCode { get; }

    public string ContentType { get; }

    public static ResponseHttpResult FromBody(string body, int statusCode, string contentType) =>
        new(body, null, statusCode, contentType);

    public static ResponseHttpResult FromStream(IAsyncEnumerable<string> chunks, int statusCode = 200, string contentType = "text/event-stream") =>
        new(null, chunks, statusCode, contentType);

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
