using System.Net;
using System.Text;
using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

public sealed class InspectionHooksSdkTests
{
    private readonly ITestOutputHelper _output;

    public InspectionHooksSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CompleteAsync_WithFakeHttpInspectionHooks_InspectsPayloadAndCapturesResponse()
    {
        string? observedModel = null;
        string? sentBody = null;
        ResponseSnapshot? snapshot = null;
        await using var services = SdkIntTestHost.CreateFakeHttpProvider(async (request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/models")
            {
                return JsonResponse(FakeModelsJson);
            }

            if (request.RequestUri?.AbsolutePath == "/responses")
            {
                sentBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();
                var response = JsonResponse(CreateTextResponseJson("Hooked fake response."));
                response.Headers.TryAddWithoutValidation("X-Fake-Trace", "fake-trace-123");
                return response;
            }

            throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
        });
        var client = services.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateContext("Say hello."), new CompletionOptions
        {
            Model = "fake-gpt",
            OnPayload = payload =>
            {
                observedModel = payload["model"]?.GetValue<string>();
                payload["instructions"] = "Rewritten by inspection hook.";
                return payload;
            },
            OnResponse = response => snapshot = response,
        });

        Assert.Equal("Hooked fake response.", Assert.IsType<TextContent>(Assert.Single(message.Content)).Text);
        Assert.Equal("fake-gpt", observedModel);
        Assert.NotNull(sentBody);
        using var body = JsonDocument.Parse(sentBody);
        Assert.Equal("Rewritten by inspection hook.", body.RootElement.GetProperty("instructions").GetString());
        Assert.NotNull(snapshot);
        Assert.Equal(200, snapshot.StatusCode);
        Assert.Equal("fake-trace-123", Assert.Single(snapshot.Headers["X-Fake-Trace"]));
        Assert.Equal("https://api.enterprise.githubcopilot.com/responses", snapshot.RequestUri?.ToString());
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task CompleteAsync_WithLiveInspectionHooks_InspectsPayloadAndCapturesResponse()
    {
        string? observedModel = null;
        ResponseSnapshot? snapshot = null;
        await using var services = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = services.GetRequiredService<ILlmSdkClient>();

        var message = await client.CompleteAsync(CreateContext("Reply with exactly: hello"), new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            MaxOutputTokens = 32,
            OnPayload = payload =>
            {
                observedModel = payload["model"]?.GetValue<string>();
                return null;
            },
            OnResponse = response => snapshot = response,
        });

        var text = string.Concat(message.Content.OfType<TextContent>().Select(static content => content.Text)).Trim();
        _output.WriteLine($"Observed model: {observedModel}");
        _output.WriteLine($"Response status: {snapshot?.StatusCode}");
        _output.WriteLine($"Response URI: {snapshot?.RequestUri}");

        Assert.Equal("gpt-5.4-mini", observedModel);
        Assert.NotNull(snapshot);
        Assert.Equal(200, snapshot.StatusCode);
        Assert.Equal("https://api.enterprise.githubcopilot.com/responses", snapshot.RequestUri?.ToString());
        Assert.NotEmpty(snapshot.Headers);
        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
    }

    private static Context CreateContext(string prompt) => new()
    {
        System = "Be concise.",
        Messages = [new UserMessage([new TextContent(prompt)])],
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string CreateTextResponseJson(string text) =>
        $$"""
        {
          "id": "resp_hooks",
          "object": "response",
          "status": "completed",
          "model": "fake-gpt",
          "output": [
            {
              "id": "msg_hooks",
              "type": "message",
              "status": "completed",
              "role": "assistant",
              "content": [
                {
                  "type": "output_text",
                  "text": {{JsonSerializer.Serialize(text, JsonDefaults.Web)}},
                  "annotations": []
                }
              ]
            }
          ]
        }
        """;

    private const string FakeModelsJson =
        """
        {
          "data": [
            {
              "id": "fake-gpt",
              "object": "model",
              "name": "Fake GPT",
              "supported_endpoints": ["/responses"]
            }
          ]
        }
        """;
}
