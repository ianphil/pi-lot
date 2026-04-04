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
        var toolCallIndex = -1;
        var started = false;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var parsed = ParseSseChunk(chunk);
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
                    var itemId = doc.RootElement.TryGetProperty("item_id", out var iid) ? iid.GetString() : null;

                    if (!started)
                    {
                        yield return SerializeChunk(id, model, new ChatChunkDelta { Role = "assistant" });
                        started = true;
                    }

                    yield return SerializeChunk(id, model, new ChatChunkDelta
                    {
                        ToolCalls =
                        [
                            new ChatChunkToolCall
                            {
                                Index = toolCallIndex,
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
                        toolCallIndex++;

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
                                    Index = toolCallIndex,
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
                    var finishReason = eventName switch
                    {
                        "response.incomplete" => "length",
                        "response.failed" => "stop",
                        _ => "stop",
                    };

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

    private static (string? EventName, string Data)? ParseSseChunk(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return null;
        }

        using var reader = new StringReader(chunk);
        string? eventName = null;
        var data = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line[5..].TrimStart());
            }
        }

        if (eventName is null && data.Length == 0)
        {
            return null;
        }

        return (eventName, data.ToString());
    }
}
