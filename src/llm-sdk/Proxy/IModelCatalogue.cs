using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

/// <summary>
/// Provides local model metadata used by the SDK.
/// </summary>
public interface IModelCatalogue
{
    /// <summary>
    /// Returns all known models.
    /// </summary>
    IReadOnlyList<ModelInfo> All();
    /// <summary>
    /// Gets metadata for a model id.
    /// </summary>
    ModelInfo Get(string id);
}
