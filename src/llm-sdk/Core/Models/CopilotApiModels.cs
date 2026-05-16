using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

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

    [JsonPropertyName("vendor")]
    public string? Vendor { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("preview")]
    public bool? Preview { get; init; }

    [JsonPropertyName("model_picker_category")]
    public string? ModelPickerCategory { get; init; }

    [JsonPropertyName("model_picker_enabled")]
    public bool? ModelPickerEnabled { get; init; }

    [JsonPropertyName("policy")]
    public ModelPolicy? Policy { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[]? SupportedEndpoints { get; init; }

    [JsonPropertyName("capabilities")]
    public ModelCapabilities? Capabilities { get; init; }
}

public sealed class ModelCapabilities
{
    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("tokenizer")]
    public string? Tokenizer { get; init; }

    [JsonPropertyName("supports")]
    public ModelSupports? Supports { get; init; }

    [JsonPropertyName("limits")]
    public ModelLimits? Limits { get; init; }
}

public sealed class ModelPolicy
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("terms")]
    public string? Terms { get; init; }
}

public sealed class ModelSupports
{
    [JsonPropertyName("adaptive_thinking")]
    public bool? AdaptiveThinking { get; init; }

    [JsonPropertyName("dimensions")]
    public bool? Dimensions { get; init; }

    [JsonPropertyName("max_thinking_budget")]
    public int? MaxThinkingBudget { get; init; }

    [JsonPropertyName("min_thinking_budget")]
    public int? MinThinkingBudget { get; init; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; init; }

    [JsonPropertyName("reasoning_effort")]
    public string[]? ReasoningEffort { get; init; }

    [JsonPropertyName("streaming")]
    public bool? Streaming { get; init; }

    [JsonPropertyName("structured_outputs")]
    public bool? StructuredOutputs { get; init; }

    [JsonPropertyName("tool_calls")]
    public bool? ToolCalls { get; init; }

    [JsonPropertyName("vision")]
    public bool? Vision { get; init; }
}

public sealed class ModelLimits
{
    [JsonPropertyName("max_context_window_tokens")]
    public int? MaxContextWindowTokens { get; init; }

    [JsonPropertyName("max_inputs")]
    public int? MaxInputs { get; init; }

    [JsonPropertyName("max_non_streaming_output_tokens")]
    public int? MaxNonStreamingOutputTokens { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("max_prompt_tokens")]
    public int? MaxPromptTokens { get; init; }

    [JsonPropertyName("vision")]
    public ModelVisionLimits? Vision { get; init; }
}

public sealed class ModelVisionLimits
{
    [JsonPropertyName("max_prompt_image_size")]
    public int? MaxPromptImageSize { get; init; }

    [JsonPropertyName("max_prompt_images")]
    public int? MaxPromptImages { get; init; }

    [JsonPropertyName("supported_media_types")]
    public string[]? SupportedMediaTypes { get; init; }
}
