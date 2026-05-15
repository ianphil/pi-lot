using System.Reflection;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSdk.Infrastructure;

public sealed class EmbeddedModelCatalogue : IModelCatalogue
{
    private const string ResourceSuffix = ".Infrastructure.models.json";

    private readonly Lazy<CatalogueData> _data = new(Load);

    public IReadOnlyList<ModelInfo> All() => _data.Value.Models;

    public ModelInfo Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _data.Value.ById.TryGetValue(id, out var model)
            ? model
            : new ModelInfo(id, id, null, null, false, false, [], null);
    }

    public static Stream? OpenResourceStream()
    {
        var assembly = typeof(EmbeddedModelCatalogue).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        return resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private static CatalogueData Load()
    {
        using var stream = OpenResourceStream();
        if (stream is null)
        {
            throw new InvalidOperationException($"The embedded model catalogue resource ending with '{ResourceSuffix}' was not found.");
        }

        var entries = JsonSerializer.Deserialize<CatalogueEntry[]>(stream, JsonDefaults.Web)
            ?? throw new InvalidOperationException("The embedded model catalogue could not be deserialized.");

        var models = entries.Select(static entry => new ModelInfo(
                entry.Id,
                entry.DisplayName ?? entry.Id,
                entry.ContextWindow,
                entry.MaxOutputTokens,
                entry.SupportsVision,
                entry.SupportsReasoning,
                entry.SupportedThinkingLevels ?? [],
                entry.Pricing))
            .ToArray();

        var byId = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entries.Zip(models))
        {
            byId[pair.First.Id] = pair.Second;
            foreach (var alias in pair.First.Aliases ?? [])
            {
                byId[alias] = pair.Second;
            }
        }

        return new CatalogueData(models, byId);
    }

    private sealed record CatalogueData(IReadOnlyList<ModelInfo> Models, IReadOnlyDictionary<string, ModelInfo> ById);

    private sealed class CatalogueEntry
    {
        public required string Id { get; init; }
        public string? DisplayName { get; init; }
        public string[]? Aliases { get; init; }
        public int? ContextWindow { get; init; }
        public int? MaxOutputTokens { get; init; }
        public bool SupportsVision { get; init; }
        public bool SupportsReasoning { get; init; }
        public ThinkingLevel[]? SupportedThinkingLevels { get; init; }
        public ModelPricing? Pricing { get; init; }
    }
}
