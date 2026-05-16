using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LlmSdk;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUpstream.Int;

internal sealed class UpstreamCaptureClient : IAsyncDisposable
{
    private const string BaseUrl = "https://api.enterprise.githubcopilot.com";
    private const string CopilotTokenEnvironmentVariable = "COPILOT_TOKEN";

    private readonly ServiceProvider _provider;
    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl) };
    private readonly string _token;

    private UpstreamCaptureClient(ServiceProvider provider, string token)
    {
        _provider = provider;
        _token = token;
    }

    public static UpstreamCaptureClient CreateAuthenticated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        var provider = services.BuildServiceProvider();

        var token = Environment.GetEnvironmentVariable(CopilotTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = provider.GetRequiredService<ICopilotCredentialStore>().GetCredential();
        }

        Assert.False(string.IsNullOrWhiteSpace(token), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");
        return new UpstreamCaptureClient(provider, token!);
    }

    public async Task<UpstreamCaptureDocument> CaptureJsonAsync(
        string name,
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = body is null ? null : JsonSerializer.Serialize(body, UpstreamCaptureJson.Options);
        using var request = CreateRequest(method, path, requestBody);
        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new UpstreamCaptureDocument
        {
            Name = name,
            BaseUrl = BaseUrl,
            Request = CaptureRequest(method, path, requestBody),
            Response = new UpstreamResponseCapture
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Headers = CaptureResponseHeaders(response),
                Body = UpstreamCaptureRedactor.RedactJsonBody(responseBody),
            },
        };
    }

    public async Task<UpstreamCaptureDocument> CaptureSseAsync(
        string name,
        string path,
        object body,
        CancellationToken cancellationToken = default)
    {
        var requestBody = JsonSerializer.Serialize(body, UpstreamCaptureJson.Options);
        using var request = CreateRequest(HttpMethod.Post, path, requestBody);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var events = await ReadSseEventsAsync(response, cancellationToken);

        return new UpstreamCaptureDocument
        {
            Name = name,
            BaseUrl = BaseUrl,
            Request = CaptureRequest(HttpMethod.Post, path, requestBody),
            Response = new UpstreamResponseCapture
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Headers = CaptureResponseHeaders(response),
                SseEvents = events,
            },
        };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _provider.DisposeAsync();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        AddPinnedHeaders(request);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static void AddPinnedHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("copilot/1.0.11 (win32) term/service");
        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "copilot-developer-cli");
    }

    private static UpstreamRequestCapture CaptureRequest(HttpMethod method, string path, string? body)
    {
        var headers = new List<KeyValuePair<string, IEnumerable<string>>>
        {
            new("Authorization", ["Bearer " + UpstreamCaptureRedactor.RedactedValue]),
            new("User-Agent", ["copilot/1.0.11 (win32) term/service"]),
            new("Copilot-Integration-Id", ["copilot-developer-cli"]),
        };

        if (body is not null)
        {
            headers.Add(new("Content-Type", ["application/json; charset=utf-8"]));
        }

        return new UpstreamRequestCapture
        {
            Method = method.Method,
            Path = path,
            Headers = UpstreamCaptureRedactor.RedactHeaders(headers),
            Body = UpstreamCaptureRedactor.RedactJsonBody(body),
        };
    }

    private static SortedDictionary<string, string[]> CaptureResponseHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers);
        return UpstreamCaptureRedactor.RedactHeaders(headers);
    }

    private static async Task<IReadOnlyList<UpstreamSseEventCapture>> ReadSseEventsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var events = new List<UpstreamSseEventCapture>();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var rawBuilder = new StringBuilder();
        var data = new List<string>();
        string? eventName = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                AddEventIfPresent();
                break;
            }

            rawBuilder.Append(line).Append('\n');
            if (line.Length == 0)
            {
                AddEventIfPresent();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].TrimStart();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line["data:".Length..].TrimStart());
            }
        }

        return events;

        void AddEventIfPresent()
        {
            if (rawBuilder.Length == 0)
            {
                return;
            }

            events.Add(new UpstreamSseEventCapture
            {
                Index = events.Count,
                Event = eventName,
                Data = data.Select(UpstreamCaptureRedactor.RedactJsonBody).OfType<System.Text.Json.Nodes.JsonNode>().ToArray(),
                Raw = UpstreamCaptureRedactor.RedactSseRaw(rawBuilder.ToString()),
            });

            rawBuilder.Clear();
            data.Clear();
            eventName = null;
        }
    }
}
