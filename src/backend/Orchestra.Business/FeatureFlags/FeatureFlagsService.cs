using Orchestra.Api.Data;

namespace Orchestra.Api.Modules.FeatureFlags;

public sealed record FeatureFlagDto(string Key, bool Enabled, string? Description, DateTime UpdatedAt);

public sealed class FeatureFlagsService(OrchestraDbContext db)
{
    public Task<List<FeatureFlagDto>> ListAsync(CancellationToken ct) =>
        db.ReadProcedureAsync("CALL ai_orchestration.api_list_feature_flags('api_rows')",
            row => new FeatureFlagDto(row.GetString(0), row.GetBoolean(1),
                row.IsDBNull(2) ? null : row.GetString(2), row.GetDateTime(3)), ct);
}
