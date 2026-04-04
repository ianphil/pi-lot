using System.Text.Json;

namespace CopilotLlm.Core.Models;

/// <summary>
/// Shared helpers for cloning <see cref="JsonElement"/> values out of their parent
/// <see cref="JsonDocument"/> so they can safely outlive the original document.
/// </summary>
public static class JsonElementHelpers
{
    /// <summary>
    /// Deep-clones a <see cref="JsonElement"/>, returning <c>default</c> when the
    /// element is <see cref="JsonValueKind.Null"/> or <see cref="JsonValueKind.Undefined"/>.
    /// </summary>
    public static JsonElement CloneOrDefault(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? default
            : Clone(element);

    /// <summary>
    /// Deep-clones a nullable <see cref="JsonElement"/>, returning <c>null</c> when
    /// the value is missing, <see cref="JsonValueKind.Null"/>, or <see cref="JsonValueKind.Undefined"/>.
    /// </summary>
    public static JsonElement? CloneOrNull(JsonElement? element) =>
        element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : Clone(element.Value);

    /// <summary>
    /// Deep-clones a <see cref="JsonElement"/> so it is independent of its parent document.
    /// </summary>
    public static JsonElement Clone(JsonElement element) =>
        JsonDocument.Parse(element.GetRawText()).RootElement.Clone();

    /// <summary>
    /// Generates a prefixed unique identifier (e.g. <c>resp_abc123…</c>).
    /// </summary>
    public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
