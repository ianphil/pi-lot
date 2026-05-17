# Diagnostics

`AssistantMessage.Diagnostics` is null on clean calls. It is populated when the
SDK recovers from, falls back from, or adjusts behavior in a way callers may want
to show or log.

```csharp
if (message.Diagnostics is { Entries.Count: > 0 } diagnostics)
{
    foreach (var entry in diagnostics.Entries)
    {
        Console.WriteLine($"{entry.Severity}: {entry.Code} - {entry.Message}");
    }
}
```

Each `DiagnosticEntry` has a severity, stable code, human message, and optional
string detail map. Detail values are sanitized before attachment.

| Code | Meaning |
|---|---|
| `image_dropped` | Image input was replaced because the model does not support vision |
| `thinking_clamped` | Requested reasoning effort was reduced to a supported level |
| `overflow_detected` | Context overflow was detected on a partial/error path |
| `silent_truncation_suspected` | Usage/model metadata suggests output was truncated |
| `partial_due_to_abort` | Cancellation returned a partial assistant message |
| `partial_due_to_error` | Stream failure returned a partial assistant message |

Diagnostics are additive to logging. Do not rely on the human message for
control flow; use the stable code.
