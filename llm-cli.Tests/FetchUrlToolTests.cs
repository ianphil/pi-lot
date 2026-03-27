#pragma warning disable OPENAI001

using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI.Responses;

namespace llm_cli.Tests;

public sealed class FetchUrlToolTests
{
    [Fact]
    public void Definition_UsesExpectedFunctionSchema()
    {
        var tool = new FetchUrlTool(new HttpClient(new StubHttpMessageHandler(_ => throw new NotSupportedException())));

        var definition = Assert.IsType<FunctionTool>(tool.Definition);

        Assert.Equal(FetchUrlTool.ToolName, definition.FunctionName);
        Assert.True(definition.StrictModeEnabled);
        Assert.Contains("\"url\"", definition.FunctionParameters.ToString(), StringComparison.Ordinal);
        Assert.Contains("HTTP or HTTPS URL", definition.FunctionDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_StripsHtmlIntoReadableText()
    {
        const string html = """
            <html>
              <head>
                <title>Example</title>
                <style>body { color: red; }</style>
                <script>console.log('ignore me');</script>
              </head>
              <body>
                <h1>Hello</h1>
                <p>World</p>
              </body>
            </html>
            """;

        var tool = new FetchUrlTool(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        })));

        var resultJson = await tool.ExecuteAsync(
            BinaryData.FromString("""{"url":"https://example.com"}"""),
            CancellationToken.None);

        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("https://example.com", root.GetProperty("url").GetString());

        var content = root.GetProperty("content").GetString();
        Assert.NotNull(content);
        Assert.Contains("Hello", content, StringComparison.Ordinal);
        Assert.Contains("World", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console.log", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStructuredErrorForInvalidScheme()
    {
        var tool = new FetchUrlTool(new HttpClient(new StubHttpMessageHandler(_ => throw new NotSupportedException())));

        var resultJson = await tool.ExecuteAsync(
            BinaryData.FromString("""{"url":"file:///c:/secret.txt"}"""),
            CancellationToken.None);

        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Contains("http://", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
