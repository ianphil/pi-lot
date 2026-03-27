#pragma warning disable OPENAI001

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Responses;

namespace llm_cli;

public sealed partial class FetchUrlTool : ILocalTool
{
    public const string ToolName = "fetch_url";

    private const int MaxContentCharacters = 20_000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public FetchUrlTool(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("llm-cli/0.2.0");
        }
    }

    public string Name => ToolName;

    public ResponseTool Definition { get; } = ResponseTool.CreateFunctionTool(
        functionName: ToolName,
        functionParameters: BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties = new
            {
                url = new
                {
                    type = "string",
                    description = "The HTTP or HTTPS URL to fetch.",
                },
            },
            required = new[] { "url" },
            additionalProperties = false,
        }),
        strictModeEnabled: true,
        functionDescription: "Fetch the contents of an HTTP or HTTPS URL and return readable text.");

    public async Task<string> ExecuteAsync(BinaryData arguments, CancellationToken cancellationToken)
    {
        FetchUrlArguments? parsedArguments;

        try
        {
            parsedArguments = JsonSerializer.Deserialize<FetchUrlArguments>(arguments, s_JsonOptions);
        }
        catch (JsonException ex)
        {
            return SerializeFailure(null, $"Invalid tool arguments: {ex.Message}");
        }

        var rawUrl = parsedArguments?.Url?.Trim();

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return SerializeFailure(rawUrl, "Missing required 'url' argument.");
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return SerializeFailure(rawUrl, "Only absolute http:// and https:// URLs are supported.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html, text/plain, application/json, application/xml;q=0.9, */*;q=0.1");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? rawUrl;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var preparedContent = PrepareContent(body, mediaType);

            if (!response.IsSuccessStatusCode)
            {
                return SerializeFailure(
                    rawUrl,
                    $"Received HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
                    (int)response.StatusCode,
                    finalUrl,
                    mediaType,
                    preparedContent.Content,
                    preparedContent.Truncated);
            }

            if (!IsSupportedTextMediaType(mediaType))
            {
                return SerializeFailure(
                    rawUrl,
                    $"Unsupported content type '{mediaType ?? "unknown"}'. Only text, HTML, JSON, and XML are supported.",
                    (int)response.StatusCode,
                    finalUrl,
                    mediaType);
            }

            return JsonSerializer.Serialize(new FetchUrlResult(
                Ok: true,
                Url: rawUrl,
                FinalUrl: finalUrl,
                StatusCode: (int)response.StatusCode,
                ContentType: mediaType,
                Content: preparedContent.Content,
                Truncated: preparedContent.Truncated,
                Error: null), s_JsonOptions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SerializeFailure(rawUrl, $"Request timed out after {RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return SerializeFailure(rawUrl, ex.Message);
        }
    }

    private static bool IsSupportedTextMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return true;
        }

        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    private static PreparedContent PrepareContent(string content, string? mediaType)
    {
        var normalized = mediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
            ? HtmlToText(content)
            : content.Trim();

        var truncated = normalized.Length > MaxContentCharacters;

        if (truncated)
        {
            normalized = $"{normalized[..MaxContentCharacters]}\n...[truncated]";
        }

        return new PreparedContent(normalized, truncated);
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = ScriptRegex().Replace(html, " ");
        var withoutStyles = StyleRegex().Replace(withoutScripts, " ");
        var withBlockBreaks = BlockRegex().Replace(withoutStyles, "\n");
        var withoutTags = TagRegex().Replace(withBlockBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedNewlines = decoded.Replace("\r\n", "\n").Replace('\r', '\n');
        var normalizedSpaces = SpaceRegex().Replace(normalizedNewlines, " ");
        return ParagraphRegex().Replace(normalizedSpaces, "\n\n").Trim();
    }

    private static string SerializeFailure(
        string? url,
        string error,
        int? statusCode = null,
        string? finalUrl = null,
        string? contentType = null,
        string? content = null,
        bool truncated = false)
        => JsonSerializer.Serialize(new FetchUrlResult(
            Ok: false,
            Url: url ?? string.Empty,
            FinalUrl: finalUrl,
            StatusCode: statusCode,
            ContentType: contentType,
            Content: content,
            Truncated: truncated,
            Error: error), s_JsonOptions);

    private sealed record FetchUrlArguments(string? Url);

    private sealed record FetchUrlResult(
        bool Ok,
        string Url,
        string? FinalUrl,
        int? StatusCode,
        string? ContentType,
        string? Content,
        bool Truncated,
        string? Error);

    private sealed record PreparedContent(string Content, bool Truncated);

    [GeneratedRegex("<script\\b[^<]*(?:(?!</script>)<[^<]*)*</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex("<style\\b[^<]*(?:(?!</style>)<[^<]*)*</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex("</?(p|div|section|article|li|ul|ol|tr|td|th|h[1-6]|br)\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("[ \\t\\f\\v]+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ParagraphRegex();
}
