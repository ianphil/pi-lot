using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Client;

/// <summary>
/// Base type for unified stream events produced by portable context streaming.
/// </summary>
public abstract record AssistantStreamEvent;

/// <summary>
/// Indicates that a stream has started and identifies the model producing output.
/// </summary>
public sealed record StreamStart(string Model) : AssistantStreamEvent;

/// <summary>
/// Contains a text delta from the assistant.
/// </summary>
public sealed record TextDelta(string Text) : AssistantStreamEvent;

/// <summary>
/// Contains a thinking or reasoning delta from the assistant.
/// </summary>
public sealed record ThinkingDelta(string Text, string? Signature = null) : AssistantStreamEvent;

/// <summary>
/// Contains a streamed tool-call argument delta and, when possible, the partially parsed JSON arguments so far.
/// </summary>
public sealed record ToolCallDelta(
    string Id,
    string Name,
    string ArgumentsJsonChunk,
    JsonElement? ParsedSoFar = null) : AssistantStreamEvent;

/// <summary>
/// Reports token usage observed during streaming.
/// </summary>
public sealed record UsageEvent(Usage Usage) : AssistantStreamEvent;

/// <summary>
/// Indicates successful stream termination and carries the final assistant message.
/// </summary>
public sealed record StreamDone(AssistantMessage FinalMessage) : AssistantStreamEvent;

/// <summary>
/// Indicates stream termination due to an error and carries the partial assistant message.
/// </summary>
public sealed record StreamError(AssistantMessage PartialMessage, string Message) : AssistantStreamEvent;
