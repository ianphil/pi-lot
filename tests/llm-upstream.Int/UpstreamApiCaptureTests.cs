using Xunit.Abstractions;

namespace LlmUpstream.Int;

[Trait("Category", "Smoke")]
public sealed class UpstreamApiCaptureTests
{
    private readonly ITestOutputHelper _output;

    public UpstreamApiCaptureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Models_Get_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "GET /models",
            HttpMethod.Get,
            "/models");

        Assert.Equal(200, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("models.get.json", capture, _output);
    }

    [Fact]
    public async Task Responses_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /responses",
            HttpMethod.Post,
            "/responses",
            new
            {
                model = "gpt-5.4-mini",
                input = "Reply with exactly: hello",
                stream = false,
            });

        Assert.Equal(200, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("responses.post.json", capture, _output);
    }

    [Fact]
    public async Task Responses_Stream_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureSseAsync(
            "POST /responses stream",
            "/responses",
            new
            {
                model = "gpt-5.4-mini",
                input = "Reply with exactly: hello",
                stream = true,
            });

        Assert.Equal(200, capture.Response.StatusCode);
        Assert.NotEmpty(capture.Response.SseEvents ?? []);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("responses.stream.sse.json", capture, _output);
    }

    [Fact]
    public async Task ChatCompletions_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /chat/completions",
            HttpMethod.Post,
            "/chat/completions",
            new
            {
                model = "gpt-5-mini",
                messages = new[]
                {
                    new { role = "user", content = "Reply with exactly: hello" },
                },
                stream = false,
            });

        Assert.Equal(200, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("chat-completions.post.json", capture, _output);
    }

    [Fact]
    public async Task ChatCompletions_Stream_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureSseAsync(
            "POST /chat/completions stream",
            "/chat/completions",
            new
            {
                model = "gpt-5-mini",
                messages = new[]
                {
                    new { role = "user", content = "Reply with exactly: hello" },
                },
                stream = true,
            });

        Assert.Equal(200, capture.Response.StatusCode);
        Assert.NotEmpty(capture.Response.SseEvents ?? []);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("chat-completions.stream.sse.json", capture, _output);
    }

    [Fact]
    public async Task AnthropicMessages_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /v1/messages",
            HttpMethod.Post,
            "/v1/messages",
            new
            {
                model = "claude-sonnet-4.6",
                max_tokens = 16,
                messages = new[]
                {
                    new { role = "user", content = "Reply with exactly: hello" },
                },
            },
            AnthropicHeaders);

        Assert.Equal(200, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("anthropic-messages.post.json", capture, _output);
    }

    [Fact]
    public async Task AnthropicMessages_Stream_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureSseAsync(
            "POST /v1/messages stream",
            "/v1/messages",
            new
            {
                model = "claude-sonnet-4.6",
                max_tokens = 16,
                stream = true,
                messages = new[]
                {
                    new { role = "user", content = "Reply with exactly: hello" },
                },
            },
            AnthropicHeaders);

        Assert.Equal(200, capture.Response.StatusCode);
        Assert.NotEmpty(capture.Response.SseEvents ?? []);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("anthropic-messages.stream.sse.json", capture, _output);
    }

    [Fact]
    public async Task Responses_WebSocket_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureWebSocketAsync(
            "WEBSOCKET /responses",
            "/responses",
            new
            {
                type = "response.create",
                model = "gpt-5.4-mini",
                input = "Reply with exactly: hello",
            });

        Assert.Equal(101, capture.Response.StatusCode);
        Assert.NotEmpty(capture.Response.WebSocketMessages ?? []);
        Assert.Contains(capture.Response.WebSocketMessages ?? [], message =>
            string.Equals(message.Data?["type"]?.GetValue<string>(), "response.completed", StringComparison.Ordinal));
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("responses.websocket.json", capture, _output);
    }

    [Fact]
    public async Task Embeddings_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /embeddings",
            HttpMethod.Post,
            "/embeddings",
            new
            {
                model = "text-embedding-3-small",
                input = "hello",
            });

        Assert.Equal(400, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("embeddings.post.json", capture, _output);
    }

    [Fact]
    public async Task EmbeddingsInference_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /embeddings inference",
            HttpMethod.Post,
            "/embeddings",
            new
            {
                model = "text-embedding-3-small-inference",
                input = "hello",
            });

        Assert.Equal(400, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("embeddings-inference.post.json", capture, _output);
    }

    [Fact]
    public async Task V1Embeddings_Post_MatchesCapture()
    {
        await using var client = UpstreamCaptureClient.CreateAuthenticated();

        var capture = await client.CaptureJsonAsync(
            "POST /v1/embeddings",
            HttpMethod.Post,
            "/v1/embeddings",
            new
            {
                model = "text-embedding-3-small",
                input = "hello",
            });

        Assert.Equal(404, capture.Response.StatusCode);
        await UpstreamSnapshotStore.AssertMatchesSnapshotAsync("v1-embeddings.post.json", capture, _output);
    }

    private static readonly IReadOnlyDictionary<string, string> AnthropicHeaders =
        new Dictionary<string, string>
        {
            ["anthropic-version"] = "2023-06-01",
        };
}
