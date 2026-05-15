using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Client;

public abstract record AssistantStreamEvent;

public sealed record StreamStart(string Model) : AssistantStreamEvent;

public sealed record TextDelta(string Text) : AssistantStreamEvent;

public sealed record ThinkingDelta(string Text, string? Signature = null) : AssistantStreamEvent;

public sealed record ToolCallDelta(
    string Id,
    string Name,
    string ArgumentsJsonChunk,
    JsonElement? ParsedSoFar = null) : AssistantStreamEvent;

public sealed record UsageEvent(Usage Usage) : AssistantStreamEvent;

public sealed record StreamDone(AssistantMessage FinalMessage) : AssistantStreamEvent;

public sealed record StreamError(AssistantMessage PartialMessage, string Message) : AssistantStreamEvent;
