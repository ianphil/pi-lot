namespace LlmSdk.Core.Models;

public sealed record ResponseSnapshot(
    int StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    TimeSpan Elapsed,
    Uri? RequestUri);
