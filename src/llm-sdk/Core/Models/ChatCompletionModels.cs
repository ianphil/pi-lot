using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

// ── OpenAI-compatible request/response types ─────────────────────────────────

public sealed record class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("messages")]
    public ChatMessage[]? Messages { get; init; }

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    [JsonPropertyName("tools")]
    public ChatToolDefinition[]? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    [JsonIgnore]
    public string? RequestId { get; init; }

    [JsonIgnore]
    public string? CorrelationId { get; init; }

    [JsonIgnore]
    public int? TimeoutMs { get; init; }

    [JsonIgnore]
    public int? MaxRetries { get; init; }

    [JsonIgnore]
    public int? MaxRetryDelayMs { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    [JsonIgnore]
    public Func<JsonNode, JsonNode?>? OnPayload { get; init; }

    [JsonIgnore]
    public Action<ResponseSnapshot>? OnResponse { get; init; }
}

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public object? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public ChatToolCall[]? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}

public sealed class ChatToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public ChatToolFunctionDefinition? Function { get; init; }
}

public sealed class ChatToolFunctionDefinition
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; init; }
}

public sealed class ChatToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public ChatToolCallFunction? Function { get; init; }
}

public sealed class ChatToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public ChatChoice[]? Choices { get; init; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }
}

public sealed class ChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public ChatChunkChoice[]? Choices { get; init; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }
}

public sealed class ChatChunkChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("delta")]
    public ChatChunkDelta? Delta { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class ChatChunkDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public ChatChunkToolCall[]? ToolCalls { get; init; }
}

public sealed class ChatChunkToolCall
{
    [JsonPropertyName("index")]
    public int? Index { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("function")]
    public ChatChunkToolCallFunction? Function { get; init; }
}

public sealed class ChatChunkToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

public sealed class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class UsageInfo
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
