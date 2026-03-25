using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LlmSvc;

/// <summary>
/// Singleton client that handles Copilot API auth and request proxying.
/// Reads the Copilot CLI credential from Windows Credential Manager and
/// sends requests directly to api.enterprise.githubcopilot.com.
/// </summary>
public sealed class CopilotClient : IDisposable
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
    private string? _token;
    private CopilotModelInfo[]? _models;
    private DateTime _modelsLastFetched = DateTime.MinValue;

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
            _logger.LogError("No Copilot CLI credential found in Credential Manager. " +
                "Run `copilot` and complete /login first.");
            return false;
        }
        _logger.LogInformation("Loaded Copilot CLI credential ({Prefix}...)", _token[..4]);
        return true;
    }

    /// <summary>
    /// Validates the current credential by calling the /models endpoint.
    /// Returns true if the token is valid.
    /// </summary>
    public async Task<bool> ValidateTokenAsync()
    {
        if (_token is null) return false;
        try
        {
            var models = await FetchModelsAsync(forceRefresh: true);
            return models.Length > 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Copilot token is invalid or expired. Attempting to reload from Credential Manager.");
            return TryLoadCredential();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate Copilot token");
            return false;
        }
    }

    /// <summary>
    /// Fetches available models from the Copilot API. Caches for 5 minutes.
    /// </summary>
    public async Task<CopilotModelInfo[]> FetchModelsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _models is not null && DateTime.UtcNow - _modelsLastFetched < TimeSpan.FromMinutes(5))
            return _models;

        var req = CreateRequest(HttpMethod.Get, "/models");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<CopilotModelsResponse>();
        _models = result?.Data?
            .Where(m => m.SupportedEndpoints is { Length: > 0 })
            .ToArray() ?? [];
        _modelsLastFetched = DateTime.UtcNow;

        _logger.LogInformation("Fetched {Count} models from Copilot API", _models.Length);
        return _models;
    }

    /// <summary>
    /// Proxies a chat completion request to the Copilot API,
    /// routing to /chat/completions or /responses based on model capabilities.
    /// </summary>
    public async Task<(string Body, int StatusCode)> ChatAsync(ChatCompletionRequest request)
    {
        if (_token is null)
            return (JsonSerializer.Serialize(MakeError("Not authenticated", "auth_error")), 401);

        if (string.IsNullOrEmpty(request.Model))
            return (JsonSerializer.Serialize(MakeError("model is required", "invalid_request")), 400);

        var models = await FetchModelsAsync();
        var model = models.FirstOrDefault(m =>
            string.Equals(m.Id, request.Model, StringComparison.OrdinalIgnoreCase));

        if (model is null)
            return (JsonSerializer.Serialize(MakeError($"Model '{request.Model}' not found", "model_not_found")), 404);

        var endpoints = model.SupportedEndpoints ?? [];
        bool useResponses = !endpoints.Contains("/chat/completions") && endpoints.Contains("/responses");
        string endpoint = useResponses ? "/responses" : "/chat/completions";

        _logger.LogInformation("Routing {Model} to {Endpoint}", request.Model, endpoint);

        var req = CreateRequest(HttpMethod.Post, endpoint);
        req.Headers.TryAddWithoutValidation("X-Initiator", "user");
        req.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");

        object payload;
        if (useResponses)
        {
            // Convert messages → input format for Responses API
            var input = request.Messages?.Select(m => new { role = m.Role, content = m.Content }).ToArray()
                ?? Array.Empty<object>();

            payload = new
            {
                model = request.Model,
                input,
                stream = false,
                max_output_tokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
                temperature = request.Temperature,
                top_p = request.TopP,
            };
        }
        else
        {
            payload = new
            {
                model = request.Model,
                messages = request.Messages,
                stream = false,
                max_completion_tokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 4096,
                temperature = request.Temperature,
                top_p = request.TopP,
            };
        }

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        // If the upstream used /responses, translate back to /chat/completions format
        if (useResponses && resp.IsSuccessStatusCode)
        {
            body = TranslateResponsesToChatCompletion(body);
        }

        // On 401, try reloading credential and hint to caller
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Got 401 from Copilot API. Reloading credential.");
            TryLoadCredential();
        }

        return (body, (int)resp.StatusCode);
    }

    /// <summary>
    /// Translates a Responses API response into a ChatCompletion response
    /// so callers always see a consistent format.
    /// </summary>
    private static string TranslateResponsesToChatCompletion(string responsesBody)
    {
        var resp = JsonSerializer.Deserialize<ResponsesApiResponse>(responsesBody);
        if (resp is null) return responsesBody;

        var textOutput = resp.Output?.FirstOrDefault(o => o.Type == "message");
        var text = textOutput?.Content?.FirstOrDefault(c => c.Type == "output_text")?.Text;

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
                    Message = new ChatMessage { Role = "assistant", Content = text },
                    FinishReason = "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = resp.Usage?.InputTokens ?? 0,
                CompletionTokens = resp.Usage?.OutputTokens ?? 0,
                TotalTokens = (resp.Usage?.InputTokens ?? 0) + (resp.Usage?.OutputTokens ?? 0),
            },
        };

        return JsonSerializer.Serialize(translated);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        foreach (var (k, v) in CopilotHeaders)
            req.Headers.TryAddWithoutValidation(k, v);
        return req;
    }

    private static OpenAIErrorResponse MakeError(string message, string code) => new()
    {
        Error = new OpenAIError { Message = message, Code = code, Type = "error" }
    };

    public void Dispose() => _http.Dispose();
}
