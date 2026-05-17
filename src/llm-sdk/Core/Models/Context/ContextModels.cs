using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

/// <summary>
/// Portable conversation state used by the high-level SDK context API.
/// </summary>
public sealed record Context : IEquatable<Context>
{
    /// <summary>
    /// Optional system instruction prepended to the conversation.
    /// </summary>
    [JsonPropertyName("system")]
    public string? System { get; init; }

    /// <summary>
    /// Ordered conversation messages.
    /// </summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<Message> Messages { get; init; } = [];

    /// <summary>
    /// Tool definitions available to the model for this request.
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];

    public bool Equals(Context? other) =>
        other is not null &&
        string.Equals(System, other.System, StringComparison.Ordinal) &&
        Messages.SequenceEqual(other.Messages) &&
        Tools.SequenceEqual(other.Tools);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(System, StringComparer.Ordinal);
        AddSequenceHash(ref hash, Messages);
        AddSequenceHash(ref hash, Tools);
        return hash.ToHashCode();
    }

    private static void AddSequenceHash<T>(ref HashCode hash, IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}

/// <summary>
/// Base type for portable context messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserMessage), "user")]
[JsonDerivedType(typeof(AssistantMessage), "assistant")]
[JsonDerivedType(typeof(ToolMessage), "tool")]
public abstract record Message;

/// <summary>
/// A user message containing one or more content blocks.
/// </summary>
public sealed record UserMessage(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content) : Message
{
    public bool Equals(UserMessage? other) =>
        other is not null && Content.SequenceEqual(other.Content);

    public override int GetHashCode() => StructuralHash.GetSequenceHash(Content);
}

/// <summary>
/// An assistant response message returned by the portable context API.
/// </summary>
public sealed record AssistantMessage(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content,
    [property: JsonPropertyName("stopReason")] StopReason StopReason,
    [property: JsonPropertyName("usage")] Usage? Usage = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null) : Message
{
    /// <summary>
    /// Optional structured diagnostics. Null means the SDK did not attach any diagnostics for this message.
    /// </summary>
    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Diagnostics? Diagnostics { get; init; }

    public bool Equals(AssistantMessage? other) =>
        other is not null &&
        Content.SequenceEqual(other.Content) &&
        StopReason == other.StopReason &&
        Equals(Usage, other.Usage) &&
        string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal) &&
        Equals(Diagnostics, other.Diagnostics);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StructuralHash.GetSequenceHash(Content));
        hash.Add(StopReason);
        hash.Add(Usage);
        hash.Add(ErrorMessage, StringComparer.Ordinal);
        hash.Add(Diagnostics);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A tool result message returned to the model after executing a tool call.
/// </summary>
public sealed record ToolMessage(
    [property: JsonPropertyName("toolCallId")] string ToolCallId,
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content) : Message
{
    public bool Equals(ToolMessage? other) =>
        other is not null &&
        string.Equals(ToolCallId, other.ToolCallId, StringComparison.Ordinal) &&
        Content.SequenceEqual(other.Content);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ToolCallId, StringComparer.Ordinal);
        hash.Add(StructuralHash.GetSequenceHash(Content));
        return hash.ToHashCode();
    }
}

/// <summary>
/// Base type for portable content blocks.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContent), "text")]
[JsonDerivedType(typeof(ImageContent), "image")]
[JsonDerivedType(typeof(ThinkingContent), "thinking")]
[JsonDerivedType(typeof(ToolCallContent), "tool_call")]
[JsonDerivedType(typeof(ToolResultContent), "tool_result")]
public abstract record ContentBlock;

/// <summary>
/// Plain text content.
/// </summary>
public sealed record TextContent([property: JsonPropertyName("text")] string Text) : ContentBlock;

/// <summary>
/// Inline image content represented as base64 data.
/// </summary>
public sealed record ImageContent(
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("base64Data")] string Base64Data) : ContentBlock;

/// <summary>
/// Model thinking or reasoning content, including redacted encrypted thinking signatures when provided by Copilot.
/// </summary>
public sealed record ThinkingContent(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("redacted")] bool Redacted = false,
    [property: JsonPropertyName("signature")] string? Signature = null) : ContentBlock;

/// <summary>
/// A model-requested tool call.
/// </summary>
public sealed record ToolCallContent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("argumentsJson")] string ArgumentsJson) : ContentBlock;

/// <summary>
/// The result of a tool call.
/// </summary>
public sealed record ToolResultContent(
    [property: JsonPropertyName("toolCallId")] string ToolCallId,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("isError")] bool IsError = false) : ContentBlock;

/// <summary>
/// Describes why assistant generation stopped.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StopReason>))]
public enum StopReason
{
    Stop,
    Length,
    ToolUse,
    ContentFilter,
    Aborted,
    Error,
}

/// <summary>
/// Token usage and optional cost metadata for a model response.
/// </summary>
public sealed record Usage(
    [property: JsonPropertyName("inputTokens")] long InputTokens,
    [property: JsonPropertyName("outputTokens")] long OutputTokens,
    [property: JsonPropertyName("cacheReadTokens")] long CacheReadTokens = 0,
    [property: JsonPropertyName("cacheWriteTokens")] long CacheWriteTokens = 0,
    [property: JsonPropertyName("cost")] decimal? Cost = null);

/// <summary>
/// Defines a tool the model may call.
/// </summary>
public sealed record ToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters = null,
    [property: JsonPropertyName("strict")] bool? Strict = null)
{
    public bool Equals(ToolDefinition? other) =>
        other is not null &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(Description, other.Description, StringComparison.Ordinal) &&
        JsonEquals(Parameters, other.Parameters) &&
        Strict == other.Strict;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        hash.Add(Parameters?.GetRawText(), StringComparer.Ordinal);
        hash.Add(Strict);
        return hash.ToHashCode();
    }

    private static bool JsonEquals(JsonElement? left, JsonElement? right) =>
        left is null && right is null ||
        left is not null && right is not null &&
        string.Equals(left.Value.GetRawText(), right.Value.GetRawText(), StringComparison.Ordinal);
}

/// <summary>
/// Result of validating tool-call arguments against a tool definition.
/// </summary>
public sealed record ToolValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

/// <summary>
/// Options for the portable context completion and streaming APIs.
/// </summary>
public sealed record CompletionOptions
{
    /// <summary>
    /// Model id to use for the request. If null, the configured default model is used.
    /// </summary>
    public string? Model { get; init; }
    /// <summary>
    /// Preferred raw API shape used to satisfy the portable request.
    /// </summary>
    public CompletionApi PreferredApi { get; init; } = CompletionApi.Responses;
    /// <summary>
    /// Controls whether streaming interruptions return a partial assistant message or throw.
    /// </summary>
    public AbortMode AbortMode { get; init; } = AbortMode.ReturnPartial;
    public int? MaxOutputTokens { get; init; }
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public ToolChoice? ToolChoice { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public string? RequestId { get; init; }
    public string? CorrelationId { get; init; }
    public int? TimeoutMs { get; init; }
    public int? MaxRetries { get; init; }
    public int? MaxRetryDelayMs { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    /// <summary>
    /// Advisory prompt-cache retention. Copilot support is translated where exposed by the current API.
    /// </summary>
    public CacheRetention Cache { get; init; } = CacheRetention.None;
    /// <summary>
    /// Stable session key used for prompt-cache affinity when supported by the selected raw API.
    /// </summary>
    public string? SessionId { get; init; }
    /// <summary>
    /// Requested reasoning effort. The SDK clamps this to the selected model's supported levels.
    /// </summary>
    public ThinkingLevel? Thinking { get; init; }
    /// <summary>
    /// Optional hook that can inspect or replace the outbound payload before it is sent.
    /// </summary>
    public Func<JsonNode, JsonNode?>? OnPayload { get; init; }
    /// <summary>
    /// Optional hook invoked with a normalized snapshot of the raw response payload.
    /// </summary>
    public Action<ResponseSnapshot>? OnResponse { get; init; }
}

/// <summary>
/// Advisory prompt-cache retention preference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CacheRetention>))]
public enum CacheRetention
{
    None,
    Short,
    Long,
}

/// <summary>
/// Raw API preference for portable context requests.
/// </summary>
public enum CompletionApi
{
    Auto,
    Responses,
    ChatCompletions,
}

/// <summary>
/// Error handling mode for streams that fail after partial output has been received.
/// </summary>
public enum AbortMode
{
    ReturnPartial,
    Throw,
}

/// <summary>
/// Tool choice preference for a request.
/// </summary>
public sealed record ToolChoice(ToolChoiceKind Kind, string? FunctionName = null)
{
    public static ToolChoice Auto { get; } = new(ToolChoiceKind.Auto);
    public static ToolChoice None { get; } = new(ToolChoiceKind.None);
    public static ToolChoice Required { get; } = new(ToolChoiceKind.Required);
    public static ToolChoice Function(string name) => new(ToolChoiceKind.Function, name);
}

/// <summary>
/// Kinds of tool-choice constraints.
/// </summary>
public enum ToolChoiceKind
{
    Auto,
    None,
    Required,
    Function,
}

internal static class StructuralHash
{
    public static int GetSequenceHash<T>(IEnumerable<T> values)
    {
        var hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
