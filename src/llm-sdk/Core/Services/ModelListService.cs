using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSdk.Core.Services;

public sealed class ModelListService
{
    private readonly IModelProvider _provider;
    private readonly IModelCatalogue? _catalogue;

    public ModelListService(IModelProvider provider, IModelCatalogue? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
        _catalogue = catalogue;
    }

    public async Task<OpenAIModelListResponse> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        return new OpenAIModelListResponse
        {
            Data = (await ListModelsAsync(cancellationToken)).ToArray(),
        };
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _provider.FetchModelsAsync(cancellationToken: cancellationToken);
        return models
            .Where(model => GetProxySupportedEndpoints(model).Length > 0)
            .Select(MergeCatalogueModel)
            .ToArray();
    }

    public async Task<ModelInfo> GetModelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var models = await ListModelsAsync(cancellationToken);
        return models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? GetCatalogueModel(id);
    }

    internal static string[] GetProxySupportedEndpoints(ModelInfo model) =>
        model.SupportsResponses || model.SupportsChatCompletions
            ? ["/v1/responses", "/v1/chat/completions"]
            : [];

    private ModelInfo MergeCatalogueModel(ModelInfo model)
    {
        var catalogueModel = GetCatalogueModel(model.Id);
        return model with
        {
            OwnedBy = model.OwnedBy ?? "github-copilot",
            DisplayName = model.Name ?? model.DisplayName ?? catalogueModel.DisplayName ?? catalogueModel.Name ?? model.Id,
            ProxySupportedEndpoints = GetProxySupportedEndpoints(model),
            Capabilities = model.Capabilities ?? catalogueModel.Capabilities,
            TokenLimits = model.TokenLimits ?? catalogueModel.TokenLimits,
            Pricing = model.Pricing ?? catalogueModel.Pricing,
        };
    }

    private ModelInfo GetCatalogueModel(string id) =>
        _catalogue?.Get(id) ?? ModelInfo.Unknown(id);
}
