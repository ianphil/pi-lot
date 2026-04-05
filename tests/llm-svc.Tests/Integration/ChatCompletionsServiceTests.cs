using System.Net.Http.Json;
using System.Text.Json;
using LlmSdk.Core.Models;

namespace llm_svc.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ChatCompletionsServiceTests : IClassFixture<ResponsesWebApplicationFactory>
{
    private readonly ResponsesWebApplicationFactory _factory;

    public ChatCompletionsServiceTests(ResponsesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Stream_ChatCapableModel_PassesThroughSseChunks()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5-mini",
                Name = "GPT-5 Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chatcmpl-123\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chatcmpl-123\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chatcmpl-123\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-5-mini",
            messages = new[] { new { role = "user", content = "Hi" } },
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"content\":\"Hello\"", body);
        Assert.Contains("\"content\":\" world\"", body);
        Assert.Contains("\"finish_reason\":\"stop\"", body);
        Assert.Contains("[DONE]", body);
        Assert.NotNull(_factory.Provider.LastChatRequest);
        Assert.Null(_factory.Provider.LastResponsesRequest);
    }

    [Fact]
    public async Task Stream_DualEndpointModel_PrefersNativeChatRoute()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                Name = "GPT-5.4",
                OwnedBy = "openai",
                SupportedEndpoints = ["/responses", "/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chatcmpl-dual\",\"model\":\"gpt-5.4\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Native chat\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chatcmpl-dual\",\"model\":\"gpt-5.4\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-5.4",
            messages = new[] { new { role = "user", content = "Hi" } },
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        Assert.NotNull(_factory.Provider.LastChatRequest);
        Assert.Null(_factory.Provider.LastResponsesRequest);
    }

    [Fact]
    public async Task Stream_ResponsesOnlyModel_TranslatesResponsesEventsToChunks()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "codex-mini",
                Name = "Codex Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/responses"],
            },
        ];
        _factory.Provider.ResponsesStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "event: response.created\ndata: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_abc\",\"model\":\"codex-mini\",\"status\":\"in_progress\"}}\n\n",
                "event: response.in_progress\ndata: {\"type\":\"response.in_progress\"}\n\n",
                "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"message\",\"id\":\"msg_1\",\"role\":\"assistant\"}}\n\n",
                "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"item_id\":\"msg_1\",\"output_index\":0,\"content_index\":0,\"delta\":\"Hello\"}\n\n",
                "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"item_id\":\"msg_1\",\"output_index\":0,\"content_index\":0,\"delta\":\" from responses\"}\n\n",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_abc\",\"model\":\"codex-mini\",\"status\":\"completed\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "codex-mini",
            messages = new[] { new { role = "user", content = "Hi" } },
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"role\":\"assistant\"", body);
        Assert.Contains("\"content\":\"Hello\"", body);
        Assert.Contains("\"content\":\" from responses\"", body);
        Assert.Contains("\"finish_reason\":\"stop\"", body);
        Assert.Contains("[DONE]", body);

        Assert.NotNull(_factory.Provider.LastResponsesRequest);
        Assert.Null(_factory.Provider.LastChatRequest);
    }

    [Fact]
    public async Task Stream_ResponsesOnlyModel_WithTools_TranslatesToolCallStream()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "codex-mini",
                Name = "Codex Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/responses"],
            },
        ];
        _factory.Provider.ResponsesStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "event: response.created\ndata: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_tool\",\"model\":\"codex-mini\",\"status\":\"in_progress\"}}\n\n",
                "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_abc\",\"name\":\"get_weather\",\"arguments\":\"\"}}\n\n",
                "event: response.function_call_arguments.delta\ndata: {\"type\":\"response.function_call_arguments.delta\",\"item_id\":\"fc_1\",\"output_index\":0,\"delta\":\"{\\\"city\\\":\"}\n\n",
                "event: response.function_call_arguments.delta\ndata: {\"type\":\"response.function_call_arguments.delta\",\"item_id\":\"fc_1\",\"output_index\":0,\"delta\":\"\\\"Paris\\\"}\"}\n\n",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_tool\",\"model\":\"codex-mini\",\"status\":\"completed\",\"usage\":{\"input_tokens\":15,\"output_tokens\":8}}}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "codex-mini",
            messages = new[] { new { role = "user", content = "What's the weather?" } },
            stream = true,
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "get_weather",
                        description = "Get weather for a city",
                        parameters = new { type = "object", properties = new { city = new { type = "string" } } },
                    },
                },
            },
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"get_weather\"", body);
        Assert.Contains("call_abc", body);
        Assert.Contains("\"finish_reason\":\"tool_calls\"", body);
        Assert.Contains("[DONE]", body);

        Assert.NotNull(_factory.Provider.LastResponsesRequest);
        Assert.True(_factory.Provider.LastResponsesRequest?.Stream);
    }

    [Fact]
    public async Task Stream_ChatCapableModel_WithTools_StreamsToolCalls()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5-mini",
                Name = "GPT-5 Mini",
                OwnedBy = "openai",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chatcmpl-tools\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_xyz\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chatcmpl-tools\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"city\\\":\\\"Paris\\\"}\"}}]},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chatcmpl-tools\",\"model\":\"gpt-5-mini\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-5-mini",
            messages = new[] { new { role = "user", content = "What's the weather?" } },
            stream = true,
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "get_weather",
                        description = "Get weather for a city",
                    },
                },
            },
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"get_weather\"", body);
        Assert.Contains("call_xyz", body);
        Assert.Contains("\"finish_reason\":\"tool_calls\"", body);
        Assert.Contains("[DONE]", body);

        Assert.NotNull(_factory.Provider.LastChatRequest);
        Assert.Null(_factory.Provider.LastResponsesRequest);
    }

    private static async IAsyncEnumerable<string> AsAsyncChunks(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }
}
