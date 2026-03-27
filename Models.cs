using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmSvc;

// ── Copilot API types ────────────────────────────────────────────────────────

public sealed class CopilotModelsResponse
{
    [JsonPropertyName("data")]
    public CopilotModelInfo[]? Data { get; init; }
}

public sealed class CopilotModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[]? SupportedEndpoints { get; init; }

    [JsonPropertyName("capabilities")]
    public CopilotModelCapabilities? Capabilities { get; init; }
}

public sealed class CopilotModelCapabilities
{
    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("limits")]
    public CopilotModelLimits? Limits { get; init; }
}

public sealed class CopilotModelLimits
{
    [JsonPropertyName("max_context_window_tokens")]
    public int? MaxContextWindowTokens { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("max_prompt_tokens")]
    public int? MaxPromptTokens { get; init; }
}

// ── OpenAI-compatible request/response types ─────────────────────────────────

public sealed class ChatCompletionRequest
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

// ── OpenAI Responses API types (for /responses-only models) ──────────────────

public sealed class ResponsesApiResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("output")]
    public ResponseOutput[]? Output { get; init; }

    [JsonPropertyName("usage")]
    public ResponsesUsageInfo? Usage { get; init; }
}

public sealed class ResponseOutput
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public ResponseContent[]? Content { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

public sealed class ResponseContent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("annotations")]
    public object[]? Annotations { get; init; }
}

public sealed class ResponsesUsageInfo
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

// ── OpenAI-compatible model list response ────────────────────────────────────

public sealed class OpenAIModelListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "list";

    [JsonPropertyName("data")]
    public required OpenAIModelInfo[] Data { get; init; }
}

public sealed class OpenAIModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "model";

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[]? SupportedEndpoints { get; init; }
}

// ── Error response ───────────────────────────────────────────────────────────

public sealed class OpenAIErrorResponse
{
    [JsonPropertyName("error")]
    public required OpenAIError Error { get; init; }
}

public sealed class OpenAIError
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
