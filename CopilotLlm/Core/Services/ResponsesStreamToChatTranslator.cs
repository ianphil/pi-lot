using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Services;

public sealed class ResponsesStreamToChatTranslator
{
    public async IAsyncEnumerable<string> TranslateStream(
        IAsyncEnumerable<string> chunks,
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = $"chatcmpl-{Guid.NewGuid():N}";
        var model = request.Model ?? "unknown";
        var outputIndexToToolCallIndex = new Dictionary<int, int>();
        var nextToolCallIndex = 0;
        var started = false;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var parsed = SseChunkParser.Parse(chunk);
            if (parsed is null)
            {
                continue;
            }

            var (eventName, data) = parsed.Value;

            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            switch (eventName)
            {
                case "response.output_text.delta":
                {
                    var delta = ExtractDelta(data);
                    if (delta is not null)
                    {
                        if (!started)
                        {
                            yield return SerializeChunk(id, model, new ChatChunkDelta { Role = "assistant" });
                            started = true;
                        }

                        yield return SerializeChunk(id, model, new ChatChunkDelta { Content = delta });
                    }

                    break;
                }

                case "response.function_call_arguments.delta":
                {
                    var doc = JsonDocument.Parse(data);
                    var argDelta = doc.RootElement.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    var outputIndex = doc.RootElement.TryGetProperty("output_index", out var oi) ? oi.GetInt32() : -1;

                    if (!started)
                    {
                        yield return SerializeChunk(id, model, new ChatChunkDelta { Role = "assistant" });
                        started = true;
                    }

                    var chatIndex = outputIndexToToolCallIndex.GetValueOrDefault(outputIndex, -1);

                    yield return SerializeChunk(id, model, new ChatChunkDelta
                    {
                        ToolCalls =
                        [
                            new ChatChunkToolCall
                            {
                                Index = chatIndex,
                                Function = new ChatChunkToolCallFunction { Arguments = argDelta },
                            },
                        ],
                    });

                    break;
                }

                case "response.output_item.added":
                {
                    var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("item", out var item) &&
                        item.TryGetProperty("type", out var type) &&
                        string.Equals(type.GetString(), "function_call", StringComparison.Ordinal))
                    {
                        var outputIndex = doc.RootElement.TryGetProperty("output_index", out var oi) ? oi.GetInt32() : -1;
                        var chatIndex = nextToolCallIndex++;
                        if (outputIndex >= 0)
                        {
                            outputIndexToToolCallIndex[outputIndex] = chatIndex;
                        }

                        if (!started)
                        {
                            yield return SerializeChunk(id, model, new ChatChunkDelta { Role = "assistant" });
                            started = true;
                        }

                        var callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() : null;
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;

                        yield return SerializeChunk(id, model, new ChatChunkDelta
                        {
                            ToolCalls =
                            [
                                new ChatChunkToolCall
                                {
                                    Index = chatIndex,
                                    Id = callId,
                                    Type = "function",
                                    Function = new ChatChunkToolCallFunction { Name = name, Arguments = "" },
                                },
                            ],
                        });
                    }

                    break;
                }

                case "response.completed":
                case "response.failed":
                case "response.incomplete":
                {
                    var finishReason = DetermineFinishReason(eventName, data, outputIndexToToolCallIndex.Count > 0);

                    if (!started)
                    {
                        yield return SerializeChunk(id, model, new ChatChunkDelta { Role = "assistant" });
                        started = true;
                    }

                    yield return SerializeFinishChunk(id, model, finishReason, data);

                    break;
                }
            }
        }

        yield return "data: [DONE]\n\n";
    }

    private static string DetermineFinishReason(string eventName, string data, bool hasToolCalls)
    {
        if (eventName == "response.incomplete")
        {
            return "length";
        }

        if (eventName == "response.completed" && hasToolCalls)
        {
            return "tool_calls";
        }

        return "stop";
    }

    private static string SerializeChunk(string id, string model, ChatChunkDelta delta)
    {
        var chunk = new ChatCompletionChunk
        {
            Id = id,
            Model = model,
            Choices =
            [
                new ChatChunkChoice
                {
                    Index = 0,
                    Delta = delta,
                },
            ],
        };

        return $"data: {JsonSerializer.Serialize(chunk, JsonDefaults.Web)}\n\n";
    }

    private static string SerializeFinishChunk(string id, string model, string finishReason, string responseData)
    {
        UsageInfo? usage = null;
        try
        {
            var doc = JsonDocument.Parse(responseData);
            if (doc.RootElement.TryGetProperty("response", out var resp) &&
                resp.TryGetProperty("usage", out var usageEl))
            {
                var inputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                var outputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                usage = new UsageInfo
                {
                    PromptTokens = inputTokens,
                    CompletionTokens = outputTokens,
                    TotalTokens = inputTokens + outputTokens,
                };
            }
        }
        catch (JsonException)
        {
        }

        var chunk = new ChatCompletionChunk
        {
            Id = id,
            Model = model,
            Choices =
            [
                new ChatChunkChoice
                {
                    Index = 0,
                    Delta = new ChatChunkDelta(),
                    FinishReason = finishReason,
                },
            ],
            Usage = usage,
        };

        return $"data: {JsonSerializer.Serialize(chunk, JsonDefaults.Web)}\n\n";
    }

    private static string? ExtractDelta(string data)
    {
        try
        {
            var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("delta", out var delta))
            {
                return delta.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

}
