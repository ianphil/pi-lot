using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    /// <summary>
    /// Reads the Copilot CLI credential from Windows Credential Manager.
    /// </summary>
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

    /// <summary>
    /// Validates the current credential by calling the /models endpoint.
    /// Returns true if the token is valid.
    /// </summary>
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

    /// <summary>
    /// Fetches available models from the Copilot API. Caches for 5 minutes.
    /// </summary>
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

        var payload = new ChatCompletionRequest
        {
            Model = request.Model,
            Messages = request.Messages,
            Stream = false,
            MaxCompletionTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools,
            ToolChoice = request.ToolChoice,
        };

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

        var payload = new CreateResponseRequest
        {
            Model = request.Model,
            Input = CloneOrDefault(request.Input),
            Stream = false,
            Instructions = request.Instructions,
            MaxOutputTokens = request.MaxOutputTokens,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Tools = request.Tools,
            ToolChoice = CloneOrNull(request.ToolChoice),
            PreviousResponseId = request.PreviousResponseId,
        };

        return await SendAsync("/responses", payload, cancellationToken);
    }

    /// <summary>
    /// Proxies a chat completion request to the Copilot API,
    /// routing to /chat/completions or /responses based on model capabilities.
    /// </summary>
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
        var req = CreateRequest(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("X-Initiator", "user");
        req.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonDefaults.Web),
            Encoding.UTF8,
            "application/json");

        var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(LogEvents.TokenExpired, "Got 401 from Copilot API. Reloading credential.");
            TryLoadCredential();
        }

        return new ProxyHttpResult(body, (int)resp.StatusCode, resp.Content.Headers.ContentType?.MediaType ?? "application/json");
    }

    /// <summary>
    /// Translates a Responses API response into a ChatCompletion response
    /// so callers always see a consistent format.
    /// </summary>
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
