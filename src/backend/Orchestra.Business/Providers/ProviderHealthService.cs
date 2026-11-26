using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Providers;

public sealed record ProviderHealthDto(
    Guid ProviderId,
    string CurrentStatus,
    double? AvailabilityPercent24h,
    double? AverageLatencyMs24h,
    DateTime? LastSuccessfulCheckAt,
    DateTime? LastCheckedAt,
    string[] RecentSafeErrorCategories);

public sealed class ProviderHealthService(OrchestraDbContext db)
{
    private const string CheckEvent = "PROVIDER_HEALTH_CHECK";

    internal async Task RecordAsync(
        Guid providerId, bool? providerSucceeded, long? latencyMs, string? safeErrorCategory, CancellationToken ct)
    {
        var checkedAt = DateTime.UtcNow;
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"CALL ai_orchestration.api_record_provider_health_check({providerId}, {checkedAt}, {providerSucceeded}::boolean, {latencyMs}::bigint, {safeErrorCategory}::text)",
                ct);
            return;
        }

        db.SystemLogs.Add(new SystemLog
        {
            SystemLogId = Guid.NewGuid(),
            EventCode = CheckEvent,
            Category = "ai_diagnostics",
            Severity = providerSucceeded == true ? "INFO" : "WARNING",
            SourceService = "orchestra-api",
            Message = "Provider diagnostic completed.",
            CorrelationId = Guid.NewGuid(),
            ErrorCode = safeErrorCategory,
            Metadata = JsonSerializer.Serialize(new CheckMetadata(providerId, providerSucceeded, latencyMs),
                JsonSerializerOptions.Web),
            OccurredAt = checkedAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProviderHealthDto> GetAsync(Guid providerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var summary = db.Database.IsNpgsql()
            ? (await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_provider_health($1, $2, 'api_rows')",
                ReadSummary, ct, providerId, now)).SingleOrDefault()
            : await ReadInMemoryAsync(providerId, now, ct);

        if (summary is null)
            throw new ApiException(404, "PROVIDER_NOT_FOUND", "The provider was not found.");

        var status = summary.LatestProviderResult switch
        {
            false => "unavailable",
            true when summary.FailedChecks > 0 => "degraded",
            true => "healthy",
            null => "unknown"
        };
        var conclusiveChecks = summary.SuccessfulChecks + summary.FailedChecks;
        return new ProviderHealthDto(
            providerId,
            status,
            conclusiveChecks == 0 ? null : Math.Round(100.0 * summary.SuccessfulChecks / conclusiveChecks, 2),
            summary.AverageLatencyMs is { } average ? Math.Round(average, 2) : null,
            summary.LastSuccessfulCheckAt,
            summary.LastCheckedAt,
            summary.RecentErrors.Distinct(StringComparer.Ordinal).Take(5).ToArray());
    }

    private static HealthSummary ReadSummary(NpgsqlDataReader row) => new(
        row.IsDBNull(0) ? null : row.GetDateTime(0),
        row.IsDBNull(1) ? null : row.GetDateTime(1),
        row.IsDBNull(2) ? null : row.GetBoolean(2),
        row.GetInt64(3), row.GetInt64(4),
        row.IsDBNull(5) ? null : row.GetDouble(5),
        row.GetFieldValue<string[]>(6));

    private async Task<HealthSummary?> ReadInMemoryAsync(Guid providerId, DateTime now, CancellationToken ct)
    {
        if (!await db.AiProviders.AnyAsync(provider => provider.ProviderId == providerId, ct))
            return null;

        var logs = await db.SystemLogs.AsNoTracking()
            .Where(log => log.EventCode == CheckEvent && log.OccurredAt <= now)
            .OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.SystemLogId)
            .ToListAsync(ct);
        var checks = logs.Select(log => new
            {
                log.OccurredAt, log.ErrorCode,
                Metadata = JsonSerializer.Deserialize<CheckMetadata>(log.Metadata, JsonSerializerOptions.Web)!
            })
            .Where(check => check.Metadata.ProviderId == providerId)
            .ToArray();
        var since = now.AddHours(-24);
        var recent = checks.Where(check => check.OccurredAt >= since).ToArray();

        return new HealthSummary(
            checks.FirstOrDefault()?.OccurredAt,
            checks.FirstOrDefault(check => check.Metadata.ProviderSucceeded == true)?.OccurredAt,
            recent.FirstOrDefault(check => check.Metadata.ProviderSucceeded.HasValue)?.Metadata.ProviderSucceeded,
            recent.LongCount(check => check.Metadata.ProviderSucceeded == true),
            recent.LongCount(check => check.Metadata.ProviderSucceeded == false),
            recent.Average(check => (double?)check.Metadata.LatencyMs),
            recent.Where(check => check.ErrorCode is not null).Take(20).Select(check => check.ErrorCode!).ToArray());
    }

    private sealed record CheckMetadata(Guid ProviderId, bool? ProviderSucceeded, long? LatencyMs);

    private sealed record HealthSummary(
        DateTime? LastCheckedAt,
        DateTime? LastSuccessfulCheckAt,
        bool? LatestProviderResult,
        long SuccessfulChecks,
        long FailedChecks,
        double? AverageLatencyMs,
        string[] RecentErrors);
}
