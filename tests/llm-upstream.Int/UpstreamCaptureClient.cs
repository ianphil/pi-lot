using System.Buffers;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LlmSdk;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUpstream.Int;

internal sealed class UpstreamCaptureClient : IAsyncDisposable
{
    private const string BaseUrl = "https://api.enterprise.githubcopilot.com";
    private const string WebSocketBaseUrl = "wss://api.enterprise.githubcopilot.com";
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
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = body is null ? null : JsonSerializer.Serialize(body, UpstreamCaptureJson.Options);
        using var request = CreateRequest(method, path, requestBody, headers);
        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new UpstreamCaptureDocument
        {
            Name = name,
            BaseUrl = BaseUrl,
            Request = CaptureRequest(method, path, requestBody, headers),
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
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = JsonSerializer.Serialize(body, UpstreamCaptureJson.Options);
        using var request = CreateRequest(HttpMethod.Post, path, requestBody, headers);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var events = await ReadSseEventsAsync(response, cancellationToken);

        return new UpstreamCaptureDocument
        {
            Name = name,
            BaseUrl = BaseUrl,
            Request = CaptureRequest(HttpMethod.Post, path, requestBody, headers),
            Response = new UpstreamResponseCapture
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Headers = CaptureResponseHeaders(response),
                SseEvents = events,
            },
        };
    }

    public async Task<UpstreamCaptureDocument> CaptureWebSocketAsync(
        string name,
        string path,
        object message,
        int maxMessages = 12,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var requestBody = JsonSerializer.Serialize(message, UpstreamCaptureJson.Options);
        using var socket = new ClientWebSocket();
        AddPinnedHeaders(socket.Options);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);

        await socket.ConnectAsync(new Uri(WebSocketBaseUrl + path), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes(requestBody), WebSocketMessageType.Text, true, timeout.Token);

        var messages = await ReadWebSocketMessagesAsync(socket, maxMessages, timeout.Token);

        return new UpstreamCaptureDocument
        {
            Name = name,
            BaseUrl = WebSocketBaseUrl,
            Request = CaptureRequest(new HttpMethod("WEBSOCKET"), path, requestBody, includeContentType: false),
            Response = new UpstreamResponseCapture
            {
                StatusCode = 101,
                ReasonPhrase = "Switching Protocols",
                Headers = [],
                WebSocketMessages = messages,
            },
        };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _provider.DisposeAsync();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? body, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        AddPinnedHeaders(request);
        ApplyHeaders(request, headers);

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

    private static void AddPinnedHeaders(ClientWebSocketOptions options)
    {
        options.SetRequestHeader("User-Agent", "copilot/1.0.11 (win32) term/service");
        options.SetRequestHeader("Copilot-Integration-Id", "copilot-developer-cli");
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static UpstreamRequestCapture CaptureRequest(
        HttpMethod method,
        string path,
        string? body,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        bool includeContentType = true)
    {
        var headers = new List<KeyValuePair<string, IEnumerable<string>>>
        {
            new("Authorization", ["Bearer " + UpstreamCaptureRedactor.RedactedValue]),
            new("User-Agent", ["copilot/1.0.11 (win32) term/service"]),
            new("Copilot-Integration-Id", ["copilot-developer-cli"]),
        };

        if (body is not null && includeContentType)
        {
            headers.Add(new("Content-Type", ["application/json; charset=utf-8"]));
        }

        if (extraHeaders is not null)
        {
            headers.AddRange(extraHeaders.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, [header.Value])));
        }

        return new UpstreamRequestCapture
        {
            Method = method.Method,
            Path = path,
            Headers = UpstreamCaptureRedactor.RedactHeaders(headers),
            Body = UpstreamCaptureRedactor.RedactJsonBody(body),
        };
    }

    private static async Task<IReadOnlyList<UpstreamWebSocketMessageCapture>> ReadWebSocketMessagesAsync(
        ClientWebSocket socket,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        var messages = new List<UpstreamWebSocketMessageCapture>();
        var buffer = new byte[16 * 1024];

        while (messages.Count < maxMessages && socket.State == WebSocketState.Open)
        {
            var builder = new ArrayBufferWriter<byte>();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.Count > 0)
                {
                    builder.Write(buffer.AsSpan(0, result.Count));
                }
            }
            while (!result.EndOfMessage);

            var raw = Encoding.UTF8.GetString(builder.WrittenSpan);
            var data = result.MessageType == WebSocketMessageType.Text
                ? UpstreamCaptureRedactor.RedactJsonBody(raw)
                : null;
            messages.Add(new UpstreamWebSocketMessageCapture
            {
                Index = messages.Count,
                MessageType = result.MessageType.ToString(),
                Data = data,
                Raw = UpstreamCaptureRedactor.RedactJsonText(raw),
            });

            if (IsTerminalWebSocketMessage(data) ||
                result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }

        return messages;
    }

    private static bool IsTerminalWebSocketMessage(System.Text.Json.Nodes.JsonNode? data)
    {
        if (data is not System.Text.Json.Nodes.JsonObject obj ||
            obj["type"]?.GetValue<string>() is not { } type)
        {
            return false;
        }

        return string.Equals(type, "error", StringComparison.Ordinal) ||
            string.Equals(type, "response.completed", StringComparison.Ordinal);
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
