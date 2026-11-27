using Orchestra.Api.Modules.Models;
using Orchestra.Api.Modules.Providers;

namespace Orchestra.Api.Modules.Catalog;

public sealed record CatalogTestResult(bool Success, long LatencyMs);

public sealed class CatalogTestsService(
    ProvidersService providers, ModelsService models, AiDiagnosticsClient diagnostics)
{
    public async Task<CatalogTestResult> TestProviderAsync(Guid providerId, CancellationToken ct)
    {
        var provider = await providers.GetAsync(providerId, ct);
        return await diagnostics.TestAsync(provider, model: null, ct);
    }

    public async Task<CatalogTestResult> TestModelAsync(Guid modelId, CancellationToken ct)
    {
        var model = await models.GetByIdAsync(modelId, ct);
        var provider = await providers.GetAsync(model.ProviderId, ct);
        return await diagnostics.TestAsync(provider, model, ct);
    }
}
