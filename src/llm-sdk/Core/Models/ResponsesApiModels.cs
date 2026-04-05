using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
    };
}

public static class ResponseStatuses
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Incomplete = "incomplete";
}

public sealed class ModelDescriptor
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[] SupportedEndpoints { get; init; } = [];

    public bool SupportsResponses => SupportedEndpoints.Contains("/responses", StringComparer.OrdinalIgnoreCase);
    public bool SupportsChatCompletions => SupportedEndpoints.Contains("/chat/completions", StringComparer.OrdinalIgnoreCase);
}

public sealed class CreateResponseRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    [JsonPropertyName("tools")]
    public ResponseFunctionToolDefinition[]? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public JsonElement? ToolChoice { get; init; }

    [JsonPropertyName("previous_response_id")]
    public string? PreviousResponseId { get; init; }

    [JsonPropertyName("truncation")]
    public string? Truncation { get; init; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; init; }

    [JsonPropertyName("text")]
    public ResponseTextConfig? Text { get; init; }

    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; init; }

    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; init; }

    [JsonPropertyName("top_logprobs")]
    public int? TopLogprobs { get; init; }

    [JsonPropertyName("store")]
    public bool? Store { get; init; }

    [JsonPropertyName("background")]
    public bool? Background { get; init; }

    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; init; }

    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }

    [JsonPropertyName("max_tool_calls")]
    public int? MaxToolCalls { get; init; }

    [JsonPropertyName("reasoning")]
    public ResponseReasoning? Reasoning { get; init; }
}

public sealed class Response
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "response";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("status")]
    public string Status { get; init; } = ResponseStatuses.Completed;

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("output")]
    public ResponseItem[] Output { get; init; } = [];

    // --- NULLABLE fields: must always serialize (even as null) ---

    [JsonPropertyName("completed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? CompletedAt { get; init; }

    [JsonPropertyName("incomplete_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ResponseIncompleteDetails? IncompleteDetails { get; init; }

    [JsonPropertyName("previous_response_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PreviousResponseId { get; init; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Instructions { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ResponseError? Error { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ResponseReasoning? Reasoning { get; init; }

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ResponseUsage? Usage { get; init; }

    [JsonPropertyName("max_output_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("max_tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? MaxToolCalls { get; init; }

    [JsonPropertyName("safety_identifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SafetyIdentifier { get; init; }

    [JsonPropertyName("prompt_cache_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PromptCacheKey { get; init; }

    // --- REQUIRED non-nullable fields with defaults ---

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 1.0;

    [JsonPropertyName("top_p")]
    public double TopP { get; init; } = 1.0;

    [JsonPropertyName("tools")]
    public ResponseFunctionToolDefinition[] Tools { get; init; } = [];

    [JsonPropertyName("tool_choice")]
    public object ToolChoice { get; init; } = "auto";

    [JsonPropertyName("truncation")]
    public string Truncation { get; init; } = "disabled";

    [JsonPropertyName("parallel_tool_calls")]
    public bool ParallelToolCalls { get; init; } = true;

    [JsonPropertyName("text")]
    public ResponseTextConfig Text { get; init; } = new();

    [JsonPropertyName("presence_penalty")]
    public double PresencePenalty { get; init; }

    [JsonPropertyName("frequency_penalty")]
    public double FrequencyPenalty { get; init; }

    [JsonPropertyName("top_logprobs")]
    public int TopLogprobs { get; init; }

    [JsonPropertyName("store")]
    public bool Store { get; init; }

    [JsonPropertyName("background")]
    public bool Background { get; init; }

    [JsonPropertyName("service_tier")]
    public string ServiceTier { get; init; } = "default";

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Metadata { get; init; }
}

public sealed class ResponseIncompleteDetails
{
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class ResponseReasoning
{
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Effort { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Summary { get; init; }
}

public sealed class ResponseTextConfig
{
    [JsonPropertyName("format")]
    public ResponseTextFormat Format { get; init; } = new();
}

public sealed class ResponseTextFormat
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResponseMessageItem), "message")]
[JsonDerivedType(typeof(ResponseFunctionCallItem), "function_call")]
[JsonDerivedType(typeof(ResponseFunctionCallOutputItem), "function_call_output")]
[JsonDerivedType(typeof(ResponseReasoningItem), "reasoning")]
public abstract class ResponseItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = ResponseStatuses.Completed;
}

public sealed class ResponseMessageItem : ResponseItem
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public ResponseContentPart[] Content { get; init; } = [];
}

public sealed class ResponseFunctionCallItem : ResponseItem
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("call_id")]
    public required string CallId { get; init; }

    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "{}";
}

public sealed class ResponseFunctionCallOutputItem : ResponseItem
{
    [JsonPropertyName("call_id")]
    public required string CallId { get; init; }

    [JsonPropertyName("output")]
    public string Output { get; init; } = string.Empty;
}

public sealed class ResponseReasoningItem : ResponseItem
{
    [JsonPropertyName("summary")]
    public ResponseSummaryTextPart[]? Summary { get; init; }

    [JsonPropertyName("content")]
    public ResponseContentPart[]? Content { get; init; }

    [JsonPropertyName("encrypted_content")]
    public string? EncryptedContent { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResponseOutputTextPart), "output_text")]
[JsonDerivedType(typeof(ResponseInputTextPart), "input_text")]
[JsonDerivedType(typeof(ResponseSummaryTextPart), "summary_text")]
public abstract class ResponseContentPart
{
}

public sealed class ResponseOutputTextPart : ResponseContentPart
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("annotations")]
    public object[] Annotations { get; init; } = [];

    [JsonPropertyName("logprobs")]
    public object[] Logprobs { get; init; } = [];
}

public sealed class ResponseInputTextPart : ResponseContentPart
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class ResponseSummaryTextPart : ResponseContentPart
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class ResponseFunctionToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }

    [JsonPropertyName("strict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Strict { get; init; }
}

public sealed class ResponseUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    [JsonPropertyName("input_tokens_details")]
    public InputTokensDetails InputTokensDetails { get; init; } = new();

    [JsonPropertyName("output_tokens_details")]
    public OutputTokensDetails OutputTokensDetails { get; init; } = new();
}

public sealed class InputTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; init; }
}

public sealed class OutputTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; init; }
}

public sealed class ResponseErrorEnvelope
{
    [JsonPropertyName("error")]
    public required ResponseError Error { get; init; }
}

public sealed class ResponseError
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("param")]
    public string? Param { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

public sealed class ResponseApiException : Exception
{
    public ResponseApiException(int statusCode, ResponseError error)
        : base(error.Message)
    {
        StatusCode = statusCode;
        Error = error;
    }

    public int StatusCode { get; }
    public ResponseError Error { get; }
}
