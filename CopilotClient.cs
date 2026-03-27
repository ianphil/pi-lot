using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;

namespace LlmSvc;

/// <summary>
/// Singleton client that handles Copilot API auth and request proxying.
/// Reads the Copilot CLI credential from Windows Credential Manager and
/// sends requests directly to api.enterprise.githubcopilot.com.
/// </summary>
public sealed class CopilotClient : IDisposable, IModelProvider
{
    private const string BaseUrl = "https://api.enterprise.githubcopilot.com";
    private const string CredentialPrefix = "copilot-cli/https://github.com";

    private static readonly Dictionary<string, string> CopilotHeaders = new()
    {
        ["User-Agent"] = "copilot/1.0.11 (win32) term/service",
        ["Copilot-Integration-Id"] = "copilot-developer-cli",
    };

    private readonly HttpClient _http = new();
    private readonly ILogger<CopilotClient> _logger;
    private CopilotModelInfo[]? _models;
    private DateTime _modelsLastFetched = DateTime.MinValue;
    private string? _token;

    public CopilotClient(ILogger<CopilotClient> logger)
    {
        _logger = logger;
    }

    public bool IsAuthenticated => _token is not null;

    public bool TryLoadCredential()
    {
        _token = CredentialManager.GetCredential(CredentialPrefix);
        if (_token is null)
        {
            _logger.LogError(LogEvents.CredentialMissing, "No Copilot CLI credential found in Credential Manager. " +
                "Run `copilot` and complete /login first.");
            return false;
        }

        _logger.LogInformation(LogEvents.CredentialLoaded, "Loaded Copilot CLI credential ({Prefix}...)", _token[..4]);
        return true;
    }

    public async Task<bool> ValidateTokenAsync()
    {
        if (_token is null)
        {
            return false;
        }

        try
        {
            var models = await FetchModelsAsync(forceRefresh: true);
            return models.Length > 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(LogEvents.TokenExpired, "Copilot token is invalid or expired. Attempting to reload from Credential Manager.");
            return TryLoadCredential();
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.TokenValidationFailed, ex, "Failed to validate Copilot token");
            return false;
        }
    }

    public async Task<CopilotModelInfo[]> FetchModelsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _models is not null && DateTime.UtcNow - _modelsLastFetched < TimeSpan.FromMinutes(5))
        {
            return _models;
        }

        var req = CreateRequest(HttpMethod.Get, "/models");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<CopilotModelsResponse>();
        _models = result?.Data?
            .Where(m => m.SupportedEndpoints is { Length: > 0 })
            .ToArray() ?? [];
        _modelsLastFetched = DateTime.UtcNow;

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
            })
            .ToArray();
    }

    public async Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return new ProxyHttpResult(
                JsonSerializer.Serialize(MakeError("Not authenticated", "auth_error"), JsonDefaults.Web),
                401);
        }

        var payload = CreateChatCompletionPayload(request, stream: false);
        return await SendAsync("/chat/completions", payload, cancellationToken);
    }

    public async Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return new ProxyHttpResult(
                JsonSerializer.Serialize(new ResponseErrorEnvelope
                {
                    Error = new ResponseError
                    {
                        Message = "Not authenticated",
                        Type = "invalid_request_error",
                        Code = "auth_error",
                    },
                }, JsonDefaults.Web),
                401);
        }

        var payload = CreateResponsesPayload(request, stream: false);
        return await SendAsync("/responses", payload, cancellationToken);
    }

    public async Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return new ProxyStreamResult(
                JsonSerializer.Serialize(MakeError("Not authenticated", "auth_error"), JsonDefaults.Web),
                401,
                "application/json");
        }

        var payload = CreateChatCompletionPayload(request, stream: true);
        return await SendStreamAsync("/chat/completions", payload, cancellationToken);
    }

    public async Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return new ProxyStreamResult(
                JsonSerializer.Serialize(new ResponseErrorEnvelope
                {
                    Error = new ResponseError
                    {
                        Message = "Not authenticated",
                        Type = "invalid_request_error",
                        Code = "auth_error",
                    },
                }, JsonDefaults.Web),
                401,
                "application/json");
        }

        var payload = CreateResponsesPayload(request, stream: true);
        return await SendStreamAsync("/responses", payload, cancellationToken);
    }

    public async Task<(string Body, int StatusCode)> ChatAsync(ChatCompletionRequest request)
    {
        if (_token is null)
        {
            return (JsonSerializer.Serialize(MakeError("Not authenticated", "auth_error"), JsonDefaults.Web), 401);
        }

        if (string.IsNullOrEmpty(request.Model))
        {
            return (JsonSerializer.Serialize(MakeError("model is required", "invalid_request"), JsonDefaults.Web), 400);
        }

        var models = await FetchModelsAsync();
        var model = models.FirstOrDefault(m =>
            string.Equals(m.Id, request.Model, StringComparison.OrdinalIgnoreCase));

        if (model is null)
        {
            return (JsonSerializer.Serialize(MakeError($"Model '{request.Model}' not found", "model_not_found"), JsonDefaults.Web), 404);
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
                Input = JsonDocument.Parse(JsonSerializer.Serialize(
                    request.Messages?.Select(message => new
                    {
                        role = message.Role,
                        content = NormalizeMessageContent(message.Content),
                    }).ToArray() ?? Array.Empty<object>(),
                    JsonDefaults.Web)).RootElement.Clone(),
                Stream = false,
                MaxOutputTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
                Temperature = request.Temperature,
                TopP = request.TopP,
            };

            upstream = await SendResponsesAsync(responsesRequest);
        }
        else
        {
            upstream = await SendChatCompletionsAsync(request);
        }

        var body = upstream.Body;
        if (useResponses && upstream.StatusCode is >= 200 and < 300)
        {
            body = TranslateResponsesToChatCompletion(body);
        }

        return (body, upstream.StatusCode);
    }

    private async Task<ProxyHttpResult> SendAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var req = CreatePostRequest(path, payload);
        var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        HandleUnauthorized(resp.StatusCode);

        return new ProxyHttpResult(body, (int)resp.StatusCode, resp.Content.Headers.ContentType?.MediaType ?? "application/json");
    }

    private async Task<ProxyStreamResult> SendStreamAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var req = CreatePostRequest(path, payload);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        HandleUnauthorized(resp.StatusCode);

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "text/event-stream";
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            resp.Dispose();
            return new ProxyStreamResult(body, (int)resp.StatusCode, contentType);
        }

        return new ProxyStreamResult(null, (int)resp.StatusCode, contentType, ReadEventChunks(resp, cancellationToken));
    }

    private static async IAsyncEnumerable<string> ReadEventChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var resp = response;
        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            builder.AppendLine(line);
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
            builder.AppendLine();
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

    private HttpRequestMessage CreatePostRequest(string path, object payload)
    {
        var req = CreateRequest(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("X-Initiator", "user");
        req.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonDefaults.Web),
            Encoding.UTF8,
            "application/json");
        return req;
    }

    private void HandleUnauthorized(System.Net.HttpStatusCode statusCode)
    {
        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(LogEvents.TokenExpired, "Got 401 from Copilot API. Reloading credential.");
            TryLoadCredential();
        }
    }

    private static string TranslateResponsesToChatCompletion(string responsesBody)
    {
        var resp = JsonSerializer.Deserialize<ResponsesApiResponse>(responsesBody, JsonDefaults.Web);
        if (resp is null)
        {
            return responsesBody;
        }

        var textOutput = resp.Output?.FirstOrDefault(output => output.Type == "message");
        var text = textOutput?.Content?.FirstOrDefault(content => content.Type == "output_text")?.Text;
        var toolCalls = resp.Output?
            .Where(output => output.Type == "function_call")
            .Select(output => new ChatToolCall
            {
                Id = output.CallId ?? output.Id,
                Function = new ChatToolCallFunction
                {
                    Name = output.Name,
                    Arguments = output.Arguments,
                },
            })
            .ToArray();

        var translated = new ChatCompletionResponse
        {
            Id = resp.Id,
            Object = "chat.completion",
            Model = resp.Model,
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = text,
                        ToolCalls = toolCalls is { Length: > 0 } ? toolCalls : null,
                    },
                    FinishReason = resp.Status == ResponseStatuses.Incomplete ? "length" : "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = resp.Usage?.InputTokens ?? 0,
                CompletionTokens = resp.Usage?.OutputTokens ?? 0,
                TotalTokens = (resp.Usage?.InputTokens ?? 0) + (resp.Usage?.OutputTokens ?? 0),
            },
        };

        return JsonSerializer.Serialize(translated, JsonDefaults.Web);
    }

    private static object? NormalizeMessageContent(object? content) => content switch
    {
        null => null,
        JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<object>(item.GetRawText(), JsonDefaults.Web)
                : item.ToString())
            .ToArray(),
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
        JsonElement element => JsonSerializer.Deserialize<object>(element.GetRawText(), JsonDefaults.Web),
        _ => content,
    };

    private static JsonElement CloneOrDefault(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? default
            : JsonDocument.Parse(element.GetRawText()).RootElement.Clone();

    private static JsonElement? CloneOrNull(JsonElement? element) =>
        element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : JsonDocument.Parse(element.Value.GetRawText()).RootElement.Clone();

    private static OpenAIErrorResponse MakeError(string message, string code) => new()
    {
        Error = new OpenAIError { Message = message, Code = code, Type = "error" }
    };

    public void Dispose() => _http.Dispose();
}
