using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmSdk.Core;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using static LlmSdk.Core.Models.JsonElementHelpers;

namespace LlmSdk.Infrastructure;

/// <summary>
/// Singleton client that handles Copilot API auth and request proxying.
/// Reads the Copilot CLI credential from the configured secure store and
/// sends requests directly to api.enterprise.githubcopilot.com.
/// </summary>
public sealed class CopilotClient : IAuthProvider, IModelProvider
{
    private const string BaseUrl = "https://api.enterprise.githubcopilot.com";
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(30);

    private static readonly Dictionary<string, string> CopilotHeaders = new()
    {
        ["User-Agent"] = "copilot/1.0.11 (win32) term/service",
        ["Copilot-Integration-Id"] = "copilot-developer-cli",
    };

    private static readonly TimeSpan ModelCacheTtl = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly ICopilotCredentialStore _credentialStore;
    private readonly ILogger<CopilotClient> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _credentialLock = new();
    private CopilotModelInfo[]? _models;
    private DateTimeOffset _modelsLastFetched = DateTimeOffset.MinValue;
    private string? _token;
    private DateTimeOffset _tokenLoadedAt = DateTimeOffset.MinValue;

    public CopilotClient(
        ILogger<CopilotClient> logger,
        IHttpClientFactory httpClientFactory,
        ICopilotCredentialStore credentialStore,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _http = httpClientFactory.CreateClient(nameof(CopilotClient));
        _credentialStore = credentialStore;
        _timeProvider = timeProvider;
    }

    public bool IsAuthenticated => _token is not null;

    public bool TryLoadCredential()
    {
        lock (_credentialLock)
        {
            return TryLoadCredentialUnsafe();
        }
    }

    private bool TryLoadCredentialUnsafe()
    {
        var envToken = Environment.GetEnvironmentVariable(CopilotCredentialConstants.EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _token = envToken;
            _tokenLoadedAt = _timeProvider.GetUtcNow();
            _logger.LogInformation(LogEvents.CredentialLoaded,
                "Loaded Copilot token from {EnvironmentVariable} ({Prefix}...)",
                CopilotCredentialConstants.EnvironmentVariableName,
                FormatTokenPrefix(_token));
            return true;
        }

        var storeToken = _credentialStore.GetCredential();
        if (!string.IsNullOrWhiteSpace(storeToken))
        {
            _token = storeToken;
            _tokenLoadedAt = _timeProvider.GetUtcNow();
            _logger.LogInformation(
                LogEvents.CredentialLoaded,
                "Loaded Copilot CLI credential from {CredentialStore} ({Prefix}...)",
                _credentialStore.DisplayName,
                FormatTokenPrefix(_token));
            return true;
        }

        _token = null;
        _tokenLoadedAt = DateTimeOffset.MinValue;
        _logger.LogError(LogEvents.CredentialMissing,
            "No Copilot CLI credential found via {CredentialStore}. Set {EnvironmentVariable} or complete `copilot` /login.",
            _credentialStore.DisplayName,
            CopilotCredentialConstants.EnvironmentVariableName);
        return false;
    }

    public async Task<bool> ValidateTokenAsync()
    {
        EnsureCredential();
        if (_token is null)
        {
            return false;
        }

        try
        {
            var models = await FetchModelsAsync(forceRefresh: true);
            return models.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.TokenValidationFailed, ex, "Failed to validate Copilot token");
            return false;
        }
    }

    public async Task<CopilotModelInfo[]> FetchModelsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _models is not null && _timeProvider.GetUtcNow() - _modelsLastFetched < ModelCacheTtl)
        {
            return _models;
        }

        EnsureCredential();
        if (_token is null)
        {
            return [];
        }

        var resp = await SendModelsRequestAsync();
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && TryReloadCredentialAfterUnauthorized())
        {
            resp.Dispose();
            resp = await SendModelsRequestAsync();
        }

        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<CopilotModelsResponse>();
        _models = result?.Data?
            .Where(m => m.SupportedEndpoints is { Length: > 0 })
            .ToArray() ?? [];
        _modelsLastFetched = _timeProvider.GetUtcNow();

        _logger.LogInformation(LogEvents.ModelsFetched, "Fetched {Count} models from Copilot API", _models.Length);
        return _models;
    }

    async Task<ModelDescriptor[]> IModelProvider.FetchModelsAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var models = await FetchModelsAsync(forceRefresh);
        return models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new ModelDescriptor
            {
                Id = model.Id!,
                Name = model.Name,
                OwnedBy = "github-copilot",
                SupportedEndpoints = model.SupportedEndpoints ?? [],
                TokenLimits = model.Capabilities?.Limits is null
                    ? null
                    : new ModelTokenLimits
                    {
                        MaxContextWindowTokens = model.Capabilities.Limits.MaxContextWindowTokens,
                        MaxOutputTokens = model.Capabilities.Limits.MaxOutputTokens,
                        MaxPromptTokens = model.Capabilities.Limits.MaxPromptTokens,
                    },
            })
            .ToArray();
    }

    public async Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredential();
        if (_token is null)
        {
            return NotAuthenticatedHttpResult();
        }

        var payload = CreateChatCompletionPayload(request, stream: false);
        return await SendAsync("/chat/completions", payload, RequestOptions.From(request), cancellationToken);
    }

    public async Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredential();
        if (_token is null)
        {
            return NotAuthenticatedHttpResult();
        }

        var payload = CreateResponsesPayload(request, stream: false);
        return await SendAsync("/responses", payload, RequestOptions.From(request), cancellationToken);
    }

    public async Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredential();
        if (_token is null)
        {
            return NotAuthenticatedStreamResult();
        }

        var payload = CreateChatCompletionPayload(request, stream: true);
        return await SendStreamAsync("/chat/completions", payload, RequestOptions.From(request), cancellationToken);
    }

    public async Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredential();
        if (_token is null)
        {
            return NotAuthenticatedStreamResult();
        }

        var payload = CreateResponsesPayload(request, stream: true);
        return await SendStreamAsync("/responses", payload, RequestOptions.From(request), cancellationToken);
    }

    public async Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredential();
        if (_token is null)
        {
            return NotAuthenticatedHttpResult();
        }

        if (string.IsNullOrEmpty(request.Model))
        {
            return new ProxyHttpResult(
                JsonSerializer.Serialize(MakeError("model is required", "invalid_request"), JsonDefaults.Web), 400);
        }

        var models = await FetchModelsAsync();
        var model = models.FirstOrDefault(m =>
            string.Equals(m.Id, request.Model, StringComparison.OrdinalIgnoreCase));

        if (model is null)
        {
            return new ProxyHttpResult(
                JsonSerializer.Serialize(MakeError($"Model '{request.Model}' not found", "model_not_found"), JsonDefaults.Web), 404);
        }

        var endpoints = model.SupportedEndpoints ?? [];
        var useResponses = !endpoints.Contains("/chat/completions", StringComparer.OrdinalIgnoreCase) &&
                           endpoints.Contains("/responses", StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(LogEvents.RequestProxied, "Routing {Model} to {Endpoint}", request.Model, useResponses ? "/responses" : "/chat/completions");

        ProxyHttpResult upstream;
        if (useResponses)
        {
            var responsesRequest = new CreateResponseRequest
            {
                Model = request.Model,
                Input = JsonDocument.Parse(
                    JsonSerializer.Serialize(MapChatMessagesToResponsesInput(request.Messages), JsonDefaults.Web)).RootElement.Clone(),
                Stream = false,
                MaxOutputTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
                Temperature = request.Temperature,
                TopP = request.TopP,
                Tools = request.Tools?
                    .Where(t => t.Function is not null)
                    .Select(t => new ResponseFunctionToolDefinition
                    {
                        Name = t.Function!.Name!,
                        Description = t.Function.Description,
                        Parameters = t.Function.Parameters,
                    }).ToArray(),
                ToolChoice = request.ToolChoice is not null
                    ? JsonDocument.Parse(JsonSerializer.Serialize(request.ToolChoice, JsonDefaults.Web)).RootElement.Clone()
                    : null,
            };

            upstream = await SendResponsesAsync(responsesRequest, cancellationToken);
        }
        else
        {
            upstream = await SendChatCompletionsAsync(request, cancellationToken);
        }

        var body = upstream.Body;
        if (useResponses && upstream.StatusCode is >= 200 and < 300)
        {
            body = ChatCompletionBodyTranslator.TranslateResponseBodyToChatCompletion(body);
        }

        return new ProxyHttpResult(body, upstream.StatusCode);
    }

    private void EnsureCredential()
    {
        lock (_credentialLock)
        {
            if (_token is not null && _timeProvider.GetUtcNow() - _tokenLoadedAt < TokenTtl)
            {
                return;
            }

            TryLoadCredentialUnsafe();
        }
    }

    private static object[] MapChatMessagesToResponsesInput(ChatMessage[]? messages)
    {
        if (messages is not { Length: > 0 })
        {
            return [];
        }

        var items = new List<object>();
        foreach (var message in messages)
        {
            if (message.ToolCalls is { Length: > 0 })
            {
                foreach (var toolCall in message.ToolCalls)
                {
                    items.Add(new
                    {
                        type = "function_call",
                        call_id = toolCall.Id,
                        name = toolCall.Function?.Name,
                        arguments = toolCall.Function?.Arguments ?? "{}",
                    });
                }
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new
                {
                    type = "function_call_output",
                    call_id = message.ToolCallId,
                    output = ExtractToolOutput(message.Content),
                });
                continue;
            }

            var content = NormalizeResponsesInputContent(message.Content);
            var hasContent = content is { Length: > 0 };
            if (!hasContent && message.ToolCalls is { Length: > 0 })
            {
                continue;
            }

            items.Add(new
            {
                type = "message",
                role = message.Role,
                content = hasContent
                    ? content
                    : [new { type = "input_text", text = string.Empty }],
            });
        }

        return items.ToArray();
    }

    private static object[]? NormalizeResponsesInputContent(object? content)
    {
        var normalized = ChatCompletionBodyTranslator.NormalizeMessageContent(content);
        return normalized switch
        {
            null => null,
            string text => [new { type = "input_text", text }],
            object[] values => values.SelectMany(MapContentValue).ToArray(),
            _ => [new { type = "input_text", text = JsonSerializer.Serialize(normalized, JsonDefaults.Web) }],
        };
    }

    private static IEnumerable<object> MapContentValue(object? value)
    {
        if (value is null)
        {
            yield break;
        }

        if (value is string text)
        {
            yield return new { type = "input_text", text };
            yield break;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                yield return JsonSerializer.Deserialize<object>(element.GetRawText(), JsonDefaults.Web)
                    ?? new { type = "input_text", text = element.GetRawText() };
                yield break;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                yield return new { type = "input_text", text = element.GetString() ?? string.Empty };
                yield break;
            }
        }

        yield return new { type = "input_text", text = JsonSerializer.Serialize(value, JsonDefaults.Web) };
    }

    private static string ExtractToolOutput(object? content) => content switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(content, JsonDefaults.Web),
    };

    private async Task<ProxyHttpResult> SendAsync(string path, object payload, RequestOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(options, cancellationToken);
        var effectiveToken = timeout?.Token ?? cancellationToken;
        using var resp = await SendPostWithRetriesAsync(path, payload, options, stream: false, effectiveToken);

        var body = await resp.Content.ReadAsStringAsync(effectiveToken);
        return new ProxyHttpResult(
            body,
            (int)resp.StatusCode,
            resp.Content.Headers.ContentType?.MediaType ?? "application/json",
            CaptureHeaders(resp));
    }

    private async Task<ProxyStreamResult> SendStreamAsync(string path, object payload, RequestOptions options, CancellationToken cancellationToken)
    {
        var timeout = CreateTimeout(options, cancellationToken);
        var effectiveToken = timeout?.Token ?? cancellationToken;
        try
        {
            var resp = await SendPostWithRetriesAsync(path, payload, options, stream: true, effectiveToken);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "text/event-stream";
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(effectiveToken);
                var headers = CaptureHeaders(resp);
                resp.Dispose();
                timeout?.Dispose();
                return new ProxyStreamResult(body, (int)resp.StatusCode, contentType, headers: headers);
            }

            return new ProxyStreamResult(
                null,
                (int)resp.StatusCode,
                contentType,
                ReadEventChunks(resp, effectiveToken, timeout),
                CaptureHeaders(resp));
        }
        catch
        {
            timeout?.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendPostWithRetriesAsync(
        string path,
        object payload,
        RequestOptions options,
        bool stream,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, options.MaxRetries ?? 0);
        for (var attempt = 0; ; attempt++)
        {
            var response = stream
                ? await SendStreamPostAsync(path, payload, options, cancellationToken)
                : await SendPostAsync(path, payload, options, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && TryReloadCredentialAfterUnauthorized())
            {
                response.Dispose();
                response = stream
                    ? await SendStreamPostAsync(path, payload, options, cancellationToken)
                    : await SendPostAsync(path, payload, options, cancellationToken);
            }

            if (!ShouldRetry(response.StatusCode) || attempt >= maxRetries)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(GetRetryDelay(attempt, options), cancellationToken);
        }
    }

    private Task<HttpResponseMessage> SendPostAsync(string path, object payload, RequestOptions options, CancellationToken cancellationToken)
    {
        var req = CreatePostRequest(path, payload, options);
        return _http.SendAsync(req, cancellationToken);
    }

    private Task<HttpResponseMessage> SendModelsRequestAsync()
    {
        var req = CreateRequest(HttpMethod.Get, "/models");
        return _http.SendAsync(req);
    }

    private Task<HttpResponseMessage> SendStreamPostAsync(string path, object payload, RequestOptions options, CancellationToken cancellationToken)
    {
        var req = CreatePostRequest(path, payload, options);
        return _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async IAsyncEnumerable<string> ReadEventChunks(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        CancellationTokenSource? timeoutOwner = null)
    {
        using var timeout = timeoutOwner;
        using var resp = response;
        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            builder.Append(line).Append('\n');
            if (line.Length == 0)
            {
                var chunk = builder.ToString();
                builder.Clear();
                if (chunk.Length > 0)
                {
                    yield return chunk;
                }
            }
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
            yield return builder.ToString();
        }
    }

    private static ChatCompletionRequest CreateChatCompletionPayload(ChatCompletionRequest request, bool stream) => new()
    {
        Model = request.Model,
        Messages = request.Messages,
        Stream = stream,
        MaxCompletionTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
        MaxTokens = request.MaxTokens,
        Temperature = request.Temperature,
        TopP = request.TopP,
        Tools = request.Tools,
        ToolChoice = request.ToolChoice,
        Headers = request.Headers,
        TimeoutMs = request.TimeoutMs,
        MaxRetries = request.MaxRetries,
        MaxRetryDelayMs = request.MaxRetryDelayMs,
        Metadata = request.Metadata,
    };

    private static CreateResponseRequest CreateResponsesPayload(CreateResponseRequest request, bool stream) => new()
    {
        Model = request.Model,
        Input = CloneOrDefault(request.Input),
        Stream = stream,
        Instructions = request.Instructions,
        MaxOutputTokens = request.MaxOutputTokens,
        Temperature = request.Temperature,
        TopP = request.TopP,
        Tools = request.Tools,
        ToolChoice = CloneOrNull(request.ToolChoice),
        PreviousResponseId = request.PreviousResponseId,
        Truncation = request.Truncation,
        ParallelToolCalls = request.ParallelToolCalls,
        Text = request.Text,
        PresencePenalty = request.PresencePenalty,
        FrequencyPenalty = request.FrequencyPenalty,
        TopLogprobs = request.TopLogprobs,
        Store = request.Store,
        Background = request.Background,
        ServiceTier = request.ServiceTier,
        Metadata = request.Metadata,
        MaxToolCalls = request.MaxToolCalls,
        Reasoning = request.Reasoning,
        Headers = request.Headers,
        TimeoutMs = request.TimeoutMs,
        MaxRetries = request.MaxRetries,
        MaxRetryDelayMs = request.MaxRetryDelayMs,
    };

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        foreach (var (key, value) in CopilotHeaders)
        {
            req.Headers.TryAddWithoutValidation(key, value);
        }

        return req;
    }

    private HttpRequestMessage CreatePostRequest(string path, object payload, RequestOptions options)
    {
        var req = CreateRequest(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("X-Initiator", "user");
        req.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonDefaults.Web),
            Encoding.UTF8,
            "application/json");
        ApplyPerCallHeaders(req, options.Headers);
        return req;
    }

    private static void ApplyPerCallHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.Remove(key);
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        return headers;
    }

    private static CancellationTokenSource? CreateTimeout(RequestOptions options, CancellationToken cancellationToken)
    {
        if (options.TimeoutMs is null)
        {
            return null;
        }

        if (options.TimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.TimeoutMs), "TimeoutMs must be greater than zero.");
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(TimeSpan.FromMilliseconds(options.TimeoutMs.Value));
        return source;
    }

    private static bool ShouldRetry(System.Net.HttpStatusCode statusCode) =>
        (int)statusCode is 408 or 429 or >= 500;

    private static TimeSpan GetRetryDelay(int attempt, RequestOptions options)
    {
        var uncapped = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
        if (options.MaxRetryDelayMs is null)
        {
            return uncapped;
        }

        if (options.MaxRetryDelayMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxRetryDelayMs), "MaxRetryDelayMs must be greater than zero.");
        }

        var cap = TimeSpan.FromMilliseconds(options.MaxRetryDelayMs.Value);
        return uncapped <= cap ? uncapped : cap;
    }

    private sealed record RequestOptions(
        IReadOnlyDictionary<string, string>? Headers,
        int? TimeoutMs,
        int? MaxRetries,
        int? MaxRetryDelayMs)
    {
        public static RequestOptions From(CreateResponseRequest request) =>
            new(request.Headers, request.TimeoutMs, request.MaxRetries, request.MaxRetryDelayMs);

        public static RequestOptions From(ChatCompletionRequest request) =>
            new(request.Headers, request.TimeoutMs, request.MaxRetries, request.MaxRetryDelayMs);
    }

    private bool TryReloadCredentialAfterUnauthorized()
    {
        _logger.LogWarning(LogEvents.TokenExpired, "Got 401 from Copilot API. Reloading credential and retrying once.");
        return TryLoadCredential();
    }

    private static ProxyHttpResult NotAuthenticatedHttpResult() => new(
        JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Not authenticated",
                Type = ErrorTypes.InvalidRequestError,
                Code = ErrorCodes.AuthError,
            },
        }, JsonDefaults.Web),
        401);

    private static ProxyStreamResult NotAuthenticatedStreamResult() => new(
        JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Not authenticated",
                Type = ErrorTypes.InvalidRequestError,
                Code = ErrorCodes.AuthError,
            },
        }, JsonDefaults.Web),
        401,
        "application/json");

    private static OpenAIErrorResponse MakeError(string message, string code) => new()
    {
        Error = new OpenAIError { Message = message, Code = code, Type = "error" }
    };

    private static string FormatTokenPrefix(string token) =>
        token[..Math.Min(4, token.Length)];
}
