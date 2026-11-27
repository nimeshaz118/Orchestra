namespace Orchestra.Api.Modules.Maintenance;

public sealed record StartMaintenanceRequest(
    string? Reason,
    string? PublicMessage,
    DateTimeOffset? ExpectedEndAt,
    string? Scope,
    string? ActiveRequestPolicy);

public sealed record EndMaintenanceRequest(string? Summary);

public sealed record MaintenanceStarted(
    Guid MaintenanceId,
    bool Active,
    string Scope,
    string PublicMessage,
    Guid StartedBy,
    DateTime StartedAt,
    DateTime? ExpectedEndAt,
    int AffectedActiveRequestCount);

public sealed record MaintenanceEnded(
    Guid MaintenanceId,
    bool Active,
    Guid EndedBy,
    DateTime EndedAt,
    string[] RestoredServices,
    string[] UnavailableServices);

public sealed record SystemStatus(
    string OverallStatus,
    bool MaintenanceActive,
    string? PublicMessage,
    string[] AffectedServices,
    DateTime LastUpdatedAt);

public sealed record MaintenanceReadinessResult(bool DatabaseReady, bool AiConfigured, bool AiReady);

public interface IMaintenanceReadiness
{
    Task<MaintenanceReadinessResult> CheckAsync(CancellationToken ct);
}
