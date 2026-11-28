using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orchestra.Api.Modules.Catalog;

namespace Orchestra.Api.Modules.Maintenance;

public sealed class MaintenanceReadiness(HealthCheckService checks, AiDiagnosticsClient diagnostics)
    : IMaintenanceReadiness
{
    public async Task<MaintenanceReadinessResult> CheckAsync(CancellationToken ct)
    {
        var database = checks.CheckHealthAsync(check => check.Name == "postgresql", ct);
        var ai = diagnostics.IsReadyAsync(ct);
        await Task.WhenAll(database, ai);
        var report = await database;
        return new MaintenanceReadinessResult(
            report.Entries.TryGetValue("postgresql", out var entry) && entry.Status == HealthStatus.Healthy,
            diagnostics.IsConfigured, await ai);
    }
}
