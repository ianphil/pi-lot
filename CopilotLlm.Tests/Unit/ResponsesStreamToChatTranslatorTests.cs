using System.Text.Json;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Services;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ResponsesStreamToChatTranslatorTests
{
    [Fact]
    public async Task TranslateStream_WithInterleavedToolCalls_RoutesArgumentsToCorrectToolCallIndex()
    {
        var chunks = CreateInterleavedToolCallChunks();
        var translator = new ResponsesStreamToChatTranslator();
        var request = new ChatCompletionRequest { Model = "gpt-5.4-mini" };

        var results = new List<string>();
        await foreach (var chunk in translator.TranslateStream(ToAsyncEnumerable(chunks), request))
        {
            results.Add(chunk);
        }

        var toolCallChunks = results
            .Where(static r => r.StartsWith("data: {", StringComparison.Ordinal))
            .Select(static r => JsonDocument.Parse(r[6..]).RootElement)
            .Where(static r => r.TryGetProperty("choices", out var c) &&
                               c[0].TryGetProperty("delta", out var d) &&
                               d.TryGetProperty("tool_calls", out _))
            .ToList();

        var argDeltas = toolCallChunks
            .Where(static r =>
            {
                var tc = r.GetProperty("choices")[0].GetProperty("delta").GetProperty("tool_calls")[0];
                return tc.TryGetProperty("function", out var f) &&
                       f.TryGetProperty("arguments", out var a) &&
                       a.GetString() is { Length: > 0 };
            })
            .Select(static r =>
            {
                var tc = r.GetProperty("choices")[0].GetProperty("delta").GetProperty("tool_calls")[0];
                return new
                {
                    Index = tc.GetProperty("index").GetInt32(),
                    Args = tc.GetProperty("function").GetProperty("arguments").GetString(),
                };
            })
            .ToList();

        var tool0Args = argDeltas.Where(x => x.Index == 0).Select(x => x.Args).ToList();
        var tool1Args = argDeltas.Where(x => x.Index == 1).Select(x => x.Args).ToList();

        Assert.Contains("{\"city\":", string.Join("", tool0Args));
        Assert.Contains("{\"city\":", string.Join("", tool1Args));
        Assert.Contains("London", string.Join("", tool0Args));
        Assert.Contains("Paris", string.Join("", tool1Args));
    }

    [Fact]
    public async Task TranslateStream_WithToolCalls_EmitsToolCallsFinishReason()
    {
        var chunks = CreateToolCallCompletedChunks();
        var translator = new ResponsesStreamToChatTranslator();
        var request = new ChatCompletionRequest { Model = "gpt-5.4-mini" };

        var results = new List<string>();
        await foreach (var chunk in translator.TranslateStream(ToAsyncEnumerable(chunks), request))
        {
            results.Add(chunk);
        }

        var finishChunk = results
            .Where(static r => r.StartsWith("data: {", StringComparison.Ordinal))
            .Select(static r => JsonDocument.Parse(r[6..]).RootElement)
            .FirstOrDefault(static r => r.TryGetProperty("choices", out var c) &&
                                        c[0].TryGetProperty("finish_reason", out var fr) &&
                                        fr.ValueKind == JsonValueKind.String);

        Assert.NotEqual(default, finishChunk);
        Assert.Equal("tool_calls", finishChunk.GetProperty("choices")[0].GetProperty("finish_reason").GetString());
    }

    [Fact]
    public async Task TranslateStream_WithTextOnly_EmitsStopFinishReason()
    {
        var response = CreateResponse(ResponseStatuses.Completed, hasToolCalls: false);
        var chunks = new[]
        {
            Sse("response.output_text.delta", new { type = "response.output_text.delta", sequence_number = 1, delta = "Hi", output_index = 0, content_index = 0 }),
            Sse("response.completed", new { type = "response.completed", sequence_number = 2, response }),
        };
        var translator = new ResponsesStreamToChatTranslator();
        var request = new ChatCompletionRequest { Model = "gpt-5.4-mini" };

        var results = new List<string>();
        await foreach (var chunk in translator.TranslateStream(ToAsyncEnumerable(chunks), request))
        {
            results.Add(chunk);
        }

        var finishChunk = results
            .Where(static r => r.StartsWith("data: {", StringComparison.Ordinal))
            .Select(static r => JsonDocument.Parse(r[6..]).RootElement)
            .FirstOrDefault(static r => r.TryGetProperty("choices", out var c) &&
                                        c[0].TryGetProperty("finish_reason", out var fr) &&
                                        fr.ValueKind == JsonValueKind.String);

        Assert.NotEqual(default, finishChunk);
        Assert.Equal("stop", finishChunk.GetProperty("choices")[0].GetProperty("finish_reason").GetString());
    }

    private static string[] CreateInterleavedToolCallChunks()
    {
        var response = CreateResponse(ResponseStatuses.Completed, hasToolCalls: true);
        return
        [
            Sse("response.output_item.added", new
            {
                type = "response.output_item.added", sequence_number = 1, output_index = 0,
                item = new { type = "function_call", call_id = "call_1", name = "get_weather", id = "fc_1" },
            }),
            Sse("response.output_item.added", new
            {
                type = "response.output_item.added", sequence_number = 2, output_index = 1,
                item = new { type = "function_call", call_id = "call_2", name = "get_weather", id = "fc_2" },
            }),
            Sse("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta", sequence_number = 3,
                item_id = "fc_1", output_index = 0, delta = "{\"city\":",
            }),
            Sse("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta", sequence_number = 4,
                item_id = "fc_2", output_index = 1, delta = "{\"city\":",
            }),
            Sse("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta", sequence_number = 5,
                item_id = "fc_1", output_index = 0, delta = "\"London\"}",
            }),
            Sse("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta", sequence_number = 6,
                item_id = "fc_2", output_index = 1, delta = "\"Paris\"}",
            }),
            Sse("response.completed", new { type = "response.completed", sequence_number = 7, response }),
        ];
    }

    private static string[] CreateToolCallCompletedChunks()
    {
        var response = CreateResponse(ResponseStatuses.Completed, hasToolCalls: true);
        return
        [
            Sse("response.output_item.added", new
            {
                type = "response.output_item.added", sequence_number = 1, output_index = 0,
                item = new { type = "function_call", call_id = "call_1", name = "get_weather", id = "fc_1" },
            }),
            Sse("response.function_call_arguments.delta", new
            {
                type = "response.function_call_arguments.delta", sequence_number = 2,
                item_id = "fc_1", output_index = 0, delta = "{\"city\":\"NYC\"}",
            }),
            Sse("response.completed", new { type = "response.completed", sequence_number = 3, response }),
        ];
    }

    private static Response CreateResponse(string status, bool hasToolCalls)
    {
        var output = new List<ResponseItem>();
        if (hasToolCalls)
        {
            output.Add(new ResponseFunctionCallItem
            {
                Id = "fc_1",
                CallId = "call_1",
                Name = "get_weather",
                Arguments = "{\"city\":\"NYC\"}",
            });
        }
        else
        {
            output.Add(new ResponseMessageItem
            {
                Id = "msg_1",
                Content = [new ResponseOutputTextPart { Text = "Hi" }],
            });
        }

        return new Response
        {
            Id = "resp_1",
            Status = status,
            Model = "gpt-5.4-mini",
            Output = output.ToArray(),
            Usage = new ResponseUsage
            {
                InputTokens = 10,
                OutputTokens = 5,
                TotalTokens = 15,
            },
        };
    }

    private static string Sse(string eventName, object data)
    {
        return ResponseSseSerializer.SerializeEvent(eventName, data);
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(string[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }
}
