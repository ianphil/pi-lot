using LlmSdk.Core.Models;

namespace LlmSdk.Proxy;

public interface IModelCatalogue
{
    IReadOnlyList<ModelInfo> All();
    ModelInfo Get(string id);
}
