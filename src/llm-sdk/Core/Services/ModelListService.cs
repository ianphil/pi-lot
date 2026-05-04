using LlmSdk.Core.Models;
using LlmSdk.Proxy;

namespace LlmSdk.Core.Services;

public sealed class ModelListService
{
    private readonly IModelProvider _provider;

    public ModelListService(IModelProvider provider)
    {
        _provider = provider;
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
                })
                .ToArray()
        };
    }

    internal static string[] GetProxySupportedEndpoints(ModelDescriptor model) =>
        model.SupportsResponses || model.SupportsChatCompletions
            ? ["/v1/responses", "/v1/chat/completions"]
            : [];
}
