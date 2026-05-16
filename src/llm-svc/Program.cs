using LlmSdk;
using LlmSdk.Core;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Proxy;
using llm_svc;
using Microsoft.Extensions.Primitives;

const string UpstreamHeaderPrefix = "X-LLM-Upstream-Header-";
const string RequestIdHeader = "X-LLM-Request-Id";
const string CorrelationIdHeader = "X-LLM-Correlation-Id";
const string MetadataHeaderPrefix = "X-LLM-Metadata-";
const string TimeoutHeader = "X-LLM-Timeout-Ms";
const string MaxRetriesHeader = "X-LLM-Max-Retries";
const string MaxRetryDelayHeader = "X-LLM-Max-Retry-Delay-Ms";
const int MaxTimeoutMs = 600_000;
const int MaxRetries = 3;
const int MaxRetryDelayMs = 30_000;
const int MaxMetadataPairs = 16;
const int MaxMetadataKeyLength = 64;
const int MaxMetadataValueLength = 256;

var allowedUpstreamHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "X-Request-Id",
};

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

if (!builder.Environment.IsEnvironment("Testing") && OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "LlmProxy";
    });

    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "LlmProxy";
        settings.LogName = "LlmProxy";
    });
}

builder.Services.AddLlmSdk();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<Worker>();
}

var app = builder.Build();

app.Logger.LogInformation(LogEvents.ServiceStarted, "LlmProxy service starting.");

// ── Load credential at startup ───────────────────────────────────────────────
var auth = app.Services.GetRequiredService<IAuthProvider>();
if (!app.Environment.IsEnvironment("Testing") && !auth.TryLoadCredential())
{
    app.Logger.LogError(LogEvents.CredentialMissing, "Failed to load Copilot credential. Service will start but requests will fail.");
}

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", (IAuthProvider a) => a.IsAuthenticated
    ? Results.Ok(new { status = "healthy", authenticated = true })
    : Results.Json(new { status = "degraded", authenticated = false }, statusCode: 503));

// ── GET /v1/models — OpenAI-compatible model list ────────────────────────────
app.MapGet("/v1/models", GetModelsAsync);
app.MapGet("/models", GetModelsAsync);

// ── POST /v1/responses — unified responses surface ───────────────────────────
app.MapPost("/v1/responses", CreateResponseAsync);
app.MapPost("/responses", CreateResponseAsync);

// ── POST /v1/chat/completions — proxy to Copilot API ────────────────────────
app.MapPost("/v1/chat/completions", ProxyChatCompletionsAsync);
app.MapPost("/chat/completions", ProxyChatCompletionsAsync);

app.Run();

static async Task<IResult> GetModelsAsync(ModelListService modelList, CancellationToken cancellationToken)
{
    try
    {
        var response = await modelList.GetModelsAsync(cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new OpenAIErrorResponse
            {
                Error = new OpenAIError { Message = ex.Message, Code = "upstream_error", Type = "error" }
            },
            statusCode: 502);
    }
}

async Task<IResult> CreateResponseAsync(
    CreateResponseRequest request,
    HttpContext httpContext,
    IResponsesService responsesService,
    CancellationToken cancellationToken)
{
    if (!TryReadProxyOptions(httpContext.Request.Headers, out var options, out var error))
    {
        return error!;
    }

    request = request with
    {
        Headers = options.Headers,
        RequestId = options.RequestId,
        CorrelationId = options.CorrelationId,
        Metadata = options.Metadata,
        TimeoutMs = options.TimeoutMs,
        MaxRetries = options.MaxRetries,
        MaxRetryDelayMs = options.MaxRetryDelayMs,
    };

    return new ResponseHttpResultAdapter(await responsesService.CreateAsync(request, cancellationToken));
}

async Task<IResult> ProxyChatCompletionsAsync(
    ChatCompletionRequest request,
    HttpContext httpContext,
    IChatCompletionsService chatService,
    CancellationToken cancellationToken)
{
    if (!TryReadProxyOptions(httpContext.Request.Headers, out var options, out var error))
    {
        return error!;
    }

    request = request with
    {
        Headers = options.Headers,
        RequestId = options.RequestId,
        CorrelationId = options.CorrelationId,
        Metadata = options.Metadata,
        TimeoutMs = options.TimeoutMs,
        MaxRetries = options.MaxRetries,
        MaxRetryDelayMs = options.MaxRetryDelayMs,
    };

    return new ResponseHttpResultAdapter(await chatService.CreateAsync(request, cancellationToken));
}

bool TryReadProxyOptions(
    IHeaderDictionary headers,
    out ProxyRequestOptions options,
    out IResult? error)
{
    options = default;

    if (!TryReadBoundedInt(headers, TimeoutHeader, 1, MaxTimeoutMs, out var timeoutMs, out error) ||
        !TryReadBoundedInt(headers, MaxRetriesHeader, 0, MaxRetries, out var maxRetries, out error) ||
        !TryReadBoundedInt(headers, MaxRetryDelayHeader, 1, MaxRetryDelayMs, out var maxRetryDelayMs, out error))
    {
        return false;
    }

    var upstreamHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!TryReadOptionalSingleHeader(headers, RequestIdHeader, out var requestId, out error) ||
        !TryReadOptionalSingleHeader(headers, CorrelationIdHeader, out var correlationId, out error))
    {
        return false;
    }

    foreach (var header in headers)
    {
        if (header.Key.StartsWith(MetadataHeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadMetadataHeader(header.Key, header.Value, metadata, out error))
            {
                return false;
            }

            continue;
        }

        if (!header.Key.StartsWith(UpstreamHeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var upstreamHeaderName = header.Key[UpstreamHeaderPrefix.Length..];
        if (!allowedUpstreamHeaders.Contains(upstreamHeaderName))
        {
            error = BadRequest($"Header '{upstreamHeaderName}' is not allowed as a per-call upstream header.");
            return false;
        }

        if (!TryReadSingleHeaderValue(header.Key, header.Value, out var value, out error))
        {
            return false;
        }

        upstreamHeaders[upstreamHeaderName] = value;
    }

    options = new ProxyRequestOptions(
        upstreamHeaders.Count == 0 ? null : upstreamHeaders,
        requestId,
        correlationId,
        metadata.Count == 0 ? null : metadata,
        timeoutMs,
        maxRetries,
        maxRetryDelayMs);
    return true;
}

bool TryReadBoundedInt(
    IHeaderDictionary headers,
    string headerName,
    int min,
    int max,
    out int? value,
    out IResult? error)
{
    value = null;
    error = null;

    if (!headers.TryGetValue(headerName, out var headerValues))
    {
        return true;
    }

    if (!TryReadSingleHeaderValue(headerName, headerValues, out var rawValue, out error))
    {
        return false;
    }

    if (!int.TryParse(rawValue, out var parsed) || parsed < min || parsed > max)
    {
        error = BadRequest($"Header '{headerName}' must be an integer from {min} through {max}.");
        return false;
    }

    value = parsed;
    return true;
}

bool TryReadSingleHeaderValue(
    string headerName,
    StringValues values,
    out string value,
    out IResult? error)
{
    value = string.Empty;
    error = null;

    if (values.Count is not 1 || string.IsNullOrWhiteSpace(values[0]))
    {
        error = BadRequest($"Header '{headerName}' must have exactly one non-empty value.");
        return false;
    }

    value = values[0]!;
    return true;
}

bool TryReadOptionalSingleHeader(
    IHeaderDictionary headers,
    string headerName,
    out string? value,
    out IResult? error)
{
    value = null;
    error = null;

    if (!headers.TryGetValue(headerName, out var headerValues))
    {
        return true;
    }

    if (!TryReadSingleHeaderValue(headerName, headerValues, out var rawValue, out error))
    {
        return false;
    }

    value = rawValue;
    return true;
}

bool TryReadMetadataHeader(
    string headerName,
    StringValues values,
    Dictionary<string, string> metadata,
    out IResult? error)
{
    error = null;

    if (metadata.Count >= MaxMetadataPairs)
    {
        error = BadRequest($"At most {MaxMetadataPairs} metadata headers are allowed.");
        return false;
    }

    var key = headerName[MetadataHeaderPrefix.Length..];
    if (!IsValidMetadataKey(key))
    {
        error = BadRequest($"Metadata header '{headerName}' must use a key of 1 through {MaxMetadataKeyLength} letters, digits, '.', '_', or '-'.");
        return false;
    }

    if (metadata.ContainsKey(key))
    {
        error = BadRequest($"Metadata key '{key}' was provided more than once.");
        return false;
    }

    if (!TryReadSingleHeaderValue(headerName, values, out var value, out error))
    {
        return false;
    }

    if (value.Length > MaxMetadataValueLength)
    {
        error = BadRequest($"Metadata header '{headerName}' must be {MaxMetadataValueLength} characters or fewer.");
        return false;
    }

    metadata[key] = value;
    return true;
}

bool IsValidMetadataKey(string key)
{
    if (key.Length is 0 or > MaxMetadataKeyLength)
    {
        return false;
    }

    foreach (var ch in key)
    {
        if (!char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-')
        {
            return false;
        }
    }

    return true;
}

IResult BadRequest(string message) => Results.Json(
    new OpenAIErrorResponse
    {
        Error = new OpenAIError { Message = message, Code = "invalid_request", Type = "invalid_request_error" }
    },
    statusCode: 400);

readonly record struct ProxyRequestOptions(
    IReadOnlyDictionary<string, string>? Headers,
    string? RequestId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata,
    int? TimeoutMs,
    int? MaxRetries,
    int? MaxRetryDelayMs);

public partial class Program;

sealed class ResponseHttpResultAdapter(ResponseHttpResult result) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = result.StatusCode;
        httpContext.Response.ContentType = result.ContentType;

        if (result.Chunks is null)
        {
            if (!string.IsNullOrEmpty(result.Body))
            {
                await httpContext.Response.WriteAsync(result.Body, httpContext.RequestAborted);
            }

            return;
        }

        await foreach (var chunk in result.Chunks.WithCancellation(httpContext.RequestAborted))
        {
            await httpContext.Response.WriteAsync(chunk, httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
        }
    }
}
