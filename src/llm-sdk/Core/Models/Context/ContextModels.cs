using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

public sealed record Context : IEquatable<Context>
{
    [JsonPropertyName("system")]
    public string? System { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<Message> Messages { get; init; } = [];

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserMessage), "user")]
[JsonDerivedType(typeof(AssistantMessage), "assistant")]
[JsonDerivedType(typeof(ToolMessage), "tool")]
public abstract record Message;

public sealed record UserMessage(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content) : Message
{
    public bool Equals(UserMessage? other) =>
        other is not null && Content.SequenceEqual(other.Content);

    public override int GetHashCode() => StructuralHash.GetSequenceHash(Content);
}

public sealed record AssistantMessage(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content,
    [property: JsonPropertyName("stopReason")] StopReason StopReason,
    [property: JsonPropertyName("usage")] Usage? Usage = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null) : Message
{
    public bool Equals(AssistantMessage? other) =>
        other is not null &&
        Content.SequenceEqual(other.Content) &&
        StopReason == other.StopReason &&
        Equals(Usage, other.Usage) &&
        string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StructuralHash.GetSequenceHash(Content));
        hash.Add(StopReason);
        hash.Add(Usage);
        hash.Add(ErrorMessage, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContent), "text")]
[JsonDerivedType(typeof(ImageContent), "image")]
[JsonDerivedType(typeof(ThinkingContent), "thinking")]
[JsonDerivedType(typeof(ToolCallContent), "tool_call")]
[JsonDerivedType(typeof(ToolResultContent), "tool_result")]
public abstract record ContentBlock;

public sealed record TextContent([property: JsonPropertyName("text")] string Text) : ContentBlock;

public sealed record ImageContent(
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("base64Data")] string Base64Data) : ContentBlock;

public sealed record ThinkingContent(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("redacted")] bool Redacted = false,
    [property: JsonPropertyName("signature")] string? Signature = null) : ContentBlock;

public sealed record ToolCallContent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("argumentsJson")] string ArgumentsJson) : ContentBlock;

public sealed record ToolResultContent(
    [property: JsonPropertyName("toolCallId")] string ToolCallId,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("isError")] bool IsError = false) : ContentBlock;

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

public sealed record Usage(
    [property: JsonPropertyName("inputTokens")] long InputTokens,
    [property: JsonPropertyName("outputTokens")] long OutputTokens,
    [property: JsonPropertyName("cacheReadTokens")] long CacheReadTokens = 0,
    [property: JsonPropertyName("cacheWriteTokens")] long CacheWriteTokens = 0,
    [property: JsonPropertyName("cost")] decimal? Cost = null);

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

public sealed record CompletionOptions
{
    public string? Model { get; init; }
    public CompletionApi PreferredApi { get; init; } = CompletionApi.Responses;
    public int? MaxOutputTokens { get; init; }
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public ToolChoice? ToolChoice { get; init; }
}

public enum CompletionApi
{
    Responses,
    ChatCompletions,
}

public sealed record ToolChoice(ToolChoiceKind Kind, string? FunctionName = null)
{
    public static ToolChoice Auto { get; } = new(ToolChoiceKind.Auto);
    public static ToolChoice None { get; } = new(ToolChoiceKind.None);
    public static ToolChoice Required { get; } = new(ToolChoiceKind.Required);
    public static ToolChoice Function(string name) => new(ToolChoiceKind.Function, name);
}

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
