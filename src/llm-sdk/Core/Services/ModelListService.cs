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
        var models = await _provider.FetchModelsAsync(cancellationToken: cancellationToken);
        return new OpenAIModelListResponse
        {
            Data = models
                .Select(model => new
                {
                    Model = model,
                    ProxySupportedEndpoints = GetProxySupportedEndpoints(model),
                })
                .Where(entry => entry.ProxySupportedEndpoints.Length > 0)
                .Select(entry => new OpenAIModelInfo
                {
                    Id = entry.Model.Id,
                    OwnedBy = entry.Model.OwnedBy ?? "github-copilot",
                    Name = entry.Model.Name,
                    SupportedEndpoints = entry.Model.SupportedEndpoints,
                    ProxySupportedEndpoints = entry.ProxySupportedEndpoints,
                    TokenLimits = entry.Model.TokenLimits,
                    Pricing = GetCatalogueModel(entry.Model.Id).Pricing,
                })
                .ToArray()
        };
    }

    public async Task<IReadOnlyList<ModelInfo>> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        var models = await _provider.FetchModelsAsync(cancellationToken: cancellationToken);
        return models
            .Where(model => GetProxySupportedEndpoints(model).Length > 0)
            .Select(ToModelInfo)
            .ToArray();
    }

    internal static string[] GetProxySupportedEndpoints(ModelDescriptor model) =>
        model.SupportsResponses || model.SupportsChatCompletions
            ? ["/v1/responses", "/v1/chat/completions"]
            : [];

    private ModelInfo ToModelInfo(ModelDescriptor model)
    {
        var catalogueModel = GetCatalogueModel(model.Id);
        return catalogueModel with
        {
            Id = model.Id,
            DisplayName = model.Name ?? catalogueModel.DisplayName,
            ContextWindow = model.TokenLimits?.MaxContextWindowTokens ?? catalogueModel.ContextWindow,
            MaxOutputTokens = model.TokenLimits?.MaxOutputTokens ?? catalogueModel.MaxOutputTokens,
        };
    }

    private ModelInfo GetCatalogueModel(string id) =>
        _catalogue?.Get(id) ?? new ModelInfo(id, id, null, null, false, false, [], null);
}
