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
}
