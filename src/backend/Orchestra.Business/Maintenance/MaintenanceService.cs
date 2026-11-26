using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Maintenance;

public sealed class MaintenanceService(
    OrchestraDbContext db, MaintenanceRequests requests, IMaintenanceReadiness readiness)
{
    internal const string AuthenticatedOperations = "authenticated_operations";
    internal const string SettingKey = "system.maintenance";
    private const string AiOnly = "ai_only";
    private const string AllowFinish = "allow_finish";
    private const string Cancel = "cancel";

    public async Task<MaintenanceStarted> StartAsync(
        StartMaintenanceRequest request, string? idempotencyKey, Guid actor, Guid correlationId,
        CancellationToken ct)
    {
        var reason = RequiredText(request.Reason, "reason", 500);
        var publicMessage = RequiredText(request.PublicMessage, "publicMessage", 300);
        var scope = request.Scope is AiOnly or AuthenticatedOperations ? request.Scope
            : throw InvalidField("scope", "Use ai_only or authenticated_operations.");
        var policy = request.ActiveRequestPolicy is AllowFinish or Cancel ? request.ActiveRequestPolicy
            : throw InvalidField("activeRequestPolicy", "Use allow_finish or cancel.");
        var key = idempotencyKey?.Trim();
        if (key is { Length: 0 or > 150 })
            throw InvalidField("Idempotency-Key", "Use a key between 1 and 150 characters.");
        var expectedEndAt = request.ExpectedEndAt is { } expected ? UtcMicroseconds(expected.UtcDateTime) : (DateTime?)null;

        var keyHash = key is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        await requests.Admission.WaitAsync(ct);
        try
        {
            var affected = requests.AffectedBy(scope);
            var result = new MaintenanceStarted(Guid.NewGuid(), true, scope, publicMessage, actor,
                UtcMicroseconds(DateTime.UtcNow), expectedEndAt, affected.Length);
            var session = new MaintenanceStart(result, reason, policy, keyHash);
            var (started, created) = db.Database.IsNpgsql()
                ? await StartInPostgresAsync(session, correlationId, ct)
                : await StartInMemoryAsync(session, correlationId, ct);

            if (created && policy == Cancel)
                foreach (var active in affected)
                    active.Cancel(publicMessage);

            return started;
        }
        finally
        {
            requests.Admission.Release();
        }
    }

    public async Task<MaintenanceEnded> EndAsync(
        EndMaintenanceRequest request, Guid actor, Guid correlationId, CancellationToken ct)
    {
        var summary = RequiredText(request.Summary, "summary", 1000);
        var session = await ReadStateAsync(ct, activeOnly: true) ?? throw NotActive();

        var state = await readiness.CheckAsync(ct);
        var requiredUnavailable = new List<string>();
        if (!state.DatabaseReady)
            requiredUnavailable.Add("postgresql");
        if (!state.AiReady && (session.Scope == AiOnly || state.AiConfigured))
            requiredUnavailable.Add("ai");
        if (requiredUnavailable.Count > 0)
            throw new ApiException(409, "REQUIRED_SERVICE_NOT_READY", "Required services are still unavailable.",
                new Dictionary<string, string[]> { ["unavailableServices"] = requiredUnavailable.ToArray() },
                retryable: true);

        var endedAt = UtcMicroseconds(DateTime.UtcNow);
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_end_maintenance($1, $2, $3, $4, $5, 'api_rows')",
                row => row.GetGuid(0), ct, session.MaintenanceId, summary, actor, correlationId, endedAt);
            if (rows.Count == 0)
                throw NotActive();
        }
        else
        {
            await EndInMemoryAsync(session.MaintenanceId, summary, actor, correlationId, endedAt, ct);
        }

        var restored = AffectedServices(session.Scope).Where(service => service != "ai" || state.AiReady).ToArray();
        return new MaintenanceEnded(session.MaintenanceId, false, actor, endedAt,
            restored, state.AiReady ? [] : ["ai"]);
    }

    public async Task<SystemStatus> GetSystemStatusAsync(CancellationToken ct)
    {
        var latest = await ReadStateAsync(ct);

        return latest is { EndedAt: null }
            ? new SystemStatus("maintenance", true, latest.PublicMessage, AffectedServices(latest.Scope), latest.StartedAt)
            : new SystemStatus("operational", false, null, [], latest?.EndedAt ?? DateTime.UtcNow);
    }

    public async Task<MaintenanceRequests.ActiveRequest> AdmitAsync(bool isAiWork, CancellationToken ct)
    {
        await requests.Admission.WaitAsync(ct);
        try
        {
            var session = await ReadStateAsync(ct, activeOnly: true);
            if (session is not null && (isAiWork || session.Scope == AuthenticatedOperations))
                throw new ApiException(503, "MAINTENANCE_ACTIVE", session.PublicMessage, retryable: true);

            return requests.Track(isAiWork);
        }
        finally
        {
            requests.Admission.Release();
        }
    }

    private async Task<(MaintenanceStarted, bool)> StartInPostgresAsync(
        MaintenanceStart session, Guid correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Once the transition starts, commit and signal affected requests even if the caller disconnects.
        var rows = await db.ReadProcedureAsync(
            "CALL ai_orchestration.api_start_maintenance($1, $2, $3, $4, $5::text, $6, $7, $8, $9, $10::timestamptz, 'api_rows')",
            ReadStartTransition, CancellationToken.None,
            session.Reason, session.Result.PublicMessage, session.Result.Scope, session.ActiveRequestPolicy,
            (object?)session.StartKeyHash ?? DBNull.Value, session.Result.AffectedActiveRequestCount,
            session.Result.StartedBy, correlationId, session.Result.StartedAt,
            (object?)session.Result.ExpectedEndAt ?? DBNull.Value);
        return rows.Single();
    }

    private static (MaintenanceStarted, bool) ReadStartTransition(NpgsqlDataReader row)
    {
        var outcome = row.GetString(0);
        switch (outcome)
        {
            case "already_active": throw AlreadyActive();
            case "idempotency_conflict": throw IdempotencyConflict();
            case "started":
            case "replayed":
                return (new MaintenanceStarted(
                    row.GetGuid(1), true, row.GetString(2), row.GetString(3), row.GetGuid(4), row.GetDateTime(5),
                    row.IsDBNull(6) ? null : row.GetDateTime(6), row.GetInt32(7)), outcome == "started");
            default: throw new InvalidOperationException($"Unexpected maintenance transition outcome '{outcome}'.");
        }
    }

    private async Task<MaintenanceState?> ReadStateAsync(CancellationToken ct, bool activeOnly = false)
    {
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_maintenance_state($1, 'api_rows')",
                row => new MaintenanceState(row.GetGuid(0), row.IsDBNull(1) ? null : row.GetDateTime(1),
                    row.GetString(2), row.GetString(3), row.GetDateTime(4)), ct, activeOnly);
            return rows.SingleOrDefault();
        }

        var saved = await db.PlatformSettings.AsNoTracking().Where(setting => setting.SettingKey == SettingKey)
            .Select(setting => setting.SettingValue).SingleOrDefaultAsync(ct);
        var state = saved is null ? null : JsonSerializer.Deserialize<MaintenanceState>(saved);
        return activeOnly && state?.EndedAt is not null ? null : state;
    }

    private async Task<(MaintenanceStarted, bool)> StartInMemoryAsync(
        MaintenanceStart session, Guid correlationId, CancellationToken ct)
    {
        if (await FindRetryAsync(session, ct) is { } replay)
            return (replay, false);

        var setting = await db.PlatformSettings.FindAsync([SettingKey], ct);
        if (setting is not null && JsonSerializer.Deserialize<MaintenanceState>(setting.SettingValue) is { EndedAt: null })
            throw AlreadyActive();

        var result = session.Result;
        if (setting is null)
        {
            setting = new PlatformSetting { SettingKey = SettingKey, IsSensitive = true, CreatedAt = result.StartedAt };
            db.PlatformSettings.Add(setting);
        }
        setting.SettingValue = JsonSerializer.Serialize(new MaintenanceState(
            result.MaintenanceId, null, result.Scope, result.PublicMessage, result.StartedAt));
        setting.UpdatedAt = result.StartedAt;
        setting.UpdatedByUserId = result.StartedBy;
        AddAudit(result.MaintenanceId, result.StartedBy, correlationId, "MAINTENANCE_STARTED",
            session.Reason, result.StartedAt, JsonSerializer.Serialize(session));
        ct.ThrowIfCancellationRequested();
        await db.SaveChangesAsync(CancellationToken.None);
        return (result, true);
    }

    private async Task EndInMemoryAsync(
        Guid maintenanceId, string summary, Guid actor, Guid correlationId, DateTime endedAt, CancellationToken ct)
    {
        await requests.Admission.WaitAsync(ct);
        try
        {
            var setting = await db.PlatformSettings.FindAsync([SettingKey], ct) ?? throw NotActive();
            var session = JsonSerializer.Deserialize<MaintenanceState>(setting.SettingValue);
            if (session is not { EndedAt: null } || session.MaintenanceId != maintenanceId)
                throw NotActive();
            setting.SettingValue = JsonSerializer.Serialize(session with { EndedAt = endedAt });
            setting.UpdatedAt = endedAt;
            setting.UpdatedByUserId = actor;
            AddAudit(maintenanceId, actor, correlationId, "MAINTENANCE_ENDED", summary, endedAt, setting.SettingValue);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            requests.Admission.Release();
        }
    }

    private async Task<MaintenanceStarted?> FindRetryAsync(MaintenanceStart request, CancellationToken ct)
    {
        if (request.StartKeyHash is null)
            return null;

        var auditValues = await db.AuditLogs.AsNoTracking()
            .Where(item => item.Action == "MAINTENANCE_STARTED" && item.ActorUserId == request.Result.StartedBy)
            .Select(item => item.NewValues!).ToListAsync(ct);
        var saved = auditValues.Select(value => JsonSerializer.Deserialize<MaintenanceStart>(value))
            .SingleOrDefault(item => item?.StartKeyHash == request.StartKeyHash);
        if (saved is null)
            return null;
        if (saved.Reason != request.Reason || saved.Result.PublicMessage != request.Result.PublicMessage ||
            saved.Result.Scope != request.Result.Scope || saved.ActiveRequestPolicy != request.ActiveRequestPolicy ||
            saved.Result.ExpectedEndAt != request.Result.ExpectedEndAt)
            throw IdempotencyConflict();

        return saved.Result;
    }

    private void AddAudit(Guid maintenanceId, Guid actor, Guid correlationId, string action,
        string reason, DateTime occurredAt, string values)
    {
        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            ActorUserId = actor,
            ActorRoleCode = "SUPER_ADMIN",
            Action = action,
            TargetType = "maintenance_session",
            TargetId = maintenanceId,
            Outcome = "SUCCESS",
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            NewValues = values
        });
    }

    private static string[] AffectedServices(string scope) =>
        scope == AiOnly ? ["ai"] : ["authenticated_api", "ai"];

    private static DateTime UtcMicroseconds(DateTime value) => value.AddTicks(-(value.Ticks % 10));

    private static string RequiredText(string? value, string field, int max)
    {
        var text = value?.Trim();
        if (text is null || text.Length < 5 || text.Length > max)
            throw InvalidField(field, $"Use between 5 and {max} characters.");
        return text;
    }

    private static ApiException InvalidField(string field, string message) =>
        new(400, "VALIDATION_ERROR", "The request contains invalid fields.",
            new Dictionary<string, string[]> { [field] = [message] });

    private static ApiException AlreadyActive() =>
        new(409, "MAINTENANCE_ALREADY_ACTIVE", "A maintenance session is already active.");

    private static ApiException NotActive() =>
        new(409, "MAINTENANCE_NOT_ACTIVE", "There is no active maintenance session to end.");

    private static ApiException IdempotencyConflict() =>
        new(409, "IDEMPOTENCY_CONFLICT", "This idempotency key was used for a different request.");

    private sealed record MaintenanceState(
        Guid MaintenanceId, DateTime? EndedAt, string Scope, string PublicMessage, DateTime StartedAt);

    private sealed record MaintenanceStart(
        MaintenanceStarted Result, string Reason, string ActiveRequestPolicy, string? StartKeyHash);
}
