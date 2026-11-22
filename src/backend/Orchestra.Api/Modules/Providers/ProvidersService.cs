using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Providers;

public sealed class ProvidersService(OrchestraDbContext db, IMapper mapper)
{
    public Task<List<ProviderDto>> ListAsync(CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_providers('api_rows')",
                ReadProvider, ct)
            : ListWithEfAsync(ct);

    private async Task<List<ProviderDto>> ListWithEfAsync(CancellationToken ct)
    {
        var providers = await db.AiProviders.AsNoTracking()
            .OrderBy(p => p.ProviderName).ToListAsync(ct);
        return mapper.Map<List<ProviderDto>>(providers);
    }

    public async Task<ProviderDto> GetAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_provider($1, 'api_rows')",
                ReadProvider, ct, id);

            return rows.SingleOrDefault() ?? NotFound();
        }

        var provider = await db.AiProviders.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProviderId == id, ct);

        return provider is null ? NotFound() : mapper.Map<ProviderDto>(provider);
    }

    public async Task<ProviderDto> CreateAsync(
        CreateProviderRequest request, Guid actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var provider = new AiProvider
        {
            ProviderId = Guid.NewGuid(),
            ProviderName = ValidName(request.ProviderName),
            BaseUrl = ValidUrl(request.BaseUrl),
            CredentialReference = ValidReference(request.CredentialReference),
            TimeoutSeconds = ValidTimeout(request.TimeoutSeconds ?? 60),
            Configuration = ValidConfiguration(request.Configuration),
            Status = "INACTIVE",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = actor,
            UpdatedByUserId = actor
        };

        db.AiProviders.Add(provider);
        AddAudit(provider, actor, "PROVIDER_CREATED", null);

        await SaveAsync(ct);
        return mapper.Map<ProviderDto>(provider);
    }

    public async Task<ProviderDto> UpdateAsync(
        Guid id, UpdateProviderRequest request, Guid actor, CancellationToken ct)
    {
        var provider = await FindTrackedAsync(id, ct);

        if (request.AdditionalProperties is { Count: > 0 })
            throw Invalid("request",
                "Only providerName, baseUrl, credentialReference, " +
                "timeoutSeconds, and configuration can be changed.");

        var hasName = Present(request.ProviderName);
        var hasUrl = Present(request.BaseUrl);
        var hasReference = Present(request.CredentialReference);
        var hasTimeout = Present(request.TimeoutSeconds);
        var hasConfiguration = Present(request.Configuration);

        if (!hasName && !hasUrl && !hasReference &&
            !hasTimeout && !hasConfiguration)
            throw Invalid("request", "Provide at least one field to update.");

        var oldState = AuditState(provider);

        if (hasName)
            provider.ProviderName =
                ValidName(RequiredString(request.ProviderName, "providerName"));

        if (hasUrl)
            provider.BaseUrl =
                ValidUrl(RequiredString(request.BaseUrl, "baseUrl"));

        if (hasReference)
        {
            provider.CredentialReference =
                request.CredentialReference.ValueKind == JsonValueKind.Null
                    ? null
                    : ValidReference(RequiredString(
                        request.CredentialReference, "credentialReference"));
        }

        if (hasTimeout)
        {
            if (request.TimeoutSeconds.ValueKind != JsonValueKind.Number ||
                !request.TimeoutSeconds.TryGetInt32(out var timeout))
                throw Invalid("timeoutSeconds", "Enter a whole number from 1 to 600.");

            provider.TimeoutSeconds = ValidTimeout(timeout);
        }

        if (hasConfiguration)
            provider.Configuration = ValidConfiguration(request.Configuration);

        provider.UpdatedAt = DateTime.UtcNow;
        provider.UpdatedByUserId = actor;
        AddAudit(provider, actor, "PROVIDER_UPDATED", oldState);

        await SaveAsync(ct);
        await db.Entry(provider).ReloadAsync(ct);
        return mapper.Map<ProviderDto>(provider);
    }

    public Task<ProviderDto> EnableAsync(
        Guid id, Guid actor, CancellationToken ct) =>
        SetStatusAsync(id, "ACTIVE", actor, ct);

    public Task<ProviderDto> DisableAsync(
        Guid id, Guid actor, CancellationToken ct) =>
        SetStatusAsync(id, "INACTIVE", actor, ct);

    private async Task<ProviderDto> SetStatusAsync(
        Guid id, string status, Guid actor, CancellationToken ct)
    {
        var provider = await FindTrackedAsync(id, ct);

        // Repeated enable/disable requests have the same outcome.
        if (provider.Status == status)
            return mapper.Map<ProviderDto>(provider);

        var oldState = AuditState(provider);
        provider.Status = status;
        provider.UpdatedAt = DateTime.UtcNow;
        provider.UpdatedByUserId = actor;

        AddAudit(provider, actor,
            status == "ACTIVE" ? "PROVIDER_ENABLED" : "PROVIDER_DISABLED",
            oldState);

        await db.SaveChangesAsync(ct);
        await db.Entry(provider).ReloadAsync(ct);
        return mapper.Map<ProviderDto>(provider);
    }

    private async Task<AiProvider> FindTrackedAsync(Guid id, CancellationToken ct) =>
        await db.AiProviders.SingleOrDefaultAsync(p => p.ProviderId == id, ct)
        ?? throw new ApiException(
            404, "PROVIDER_NOT_FOUND", "The provider was not found.");

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ai_provider_provider_name_key"
        })
        {
            throw new ApiException(
                409, "PROVIDER_EXISTS",
                "A provider with this name already exists.");
        }
    }

    private void AddAudit(
        AiProvider provider, Guid actor, string action, string? oldState)
    {
        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            ActorUserId = actor,
            ActorRoleCode = "SUPER_ADMIN",
            Action = action,
            TargetType = "AI_PROVIDER",
            TargetId = provider.ProviderId,
            OldValues = oldState,
            NewValues = AuditState(provider),
            Outcome = "SUCCESS",
            CorrelationId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow
        });
    }

    // Keep secrets and arbitrary configuration out of audit values.
    private static string AuditState(AiProvider p) =>
        JsonSerializer.Serialize(new
        {
            providerName = p.ProviderName,
            status = p.Status,
            timeoutSeconds = p.TimeoutSeconds
        });

    private static bool Present(JsonElement value) =>
        value.ValueKind != JsonValueKind.Undefined;

    private static string RequiredString(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid(field, "Enter a string value.");

    private static string ValidName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            throw Invalid("providerName", "Enter a name of 1 to 150 characters.");
        return name;
    }

    private static string ValidUrl(string? value)
    {
        var text = value?.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw Invalid("baseUrl",
                "Enter an HTTPS base URL without credentials, a query, or a fragment.");

        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string? ValidReference(string? value)
    {
        if (value is null) return null;
        var reference = value.Trim();
        if (reference.Length is < 1 or > 150 ||
            !Regex.IsMatch(reference, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw Invalid("credentialReference",
                "Enter a secret name such as NVIDIA_API_KEY, not a secret value.");
        return reference;
    }

    private static int ValidTimeout(int value) =>
        value is >= 1 and <= 600
            ? value
            : throw Invalid("timeoutSeconds", "Enter a whole number from 1 to 600.");

    private static string ValidConfiguration(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            return "{}";

        if (value.ValueKind != JsonValueKind.Object ||
            value.GetRawText().Length > 16_384)
            throw Invalid("configuration",
                "Enter a JSON object of at most 16,384 characters.");

        return value.GetRawText();
    }

    private static ApiException Invalid(string field, string message) =>
        new(422, "INVALID_PROVIDER", "The provider has invalid fields.",
            new Dictionary<string, string[]> { [field] = [message] });

    private static ProviderDto NotFound() =>
        throw new ApiException(
            404, "PROVIDER_NOT_FOUND", "The provider was not found.");

    // Column order must match both SELECT statements in migration 004.
    private static ProviderDto ReadProvider(NpgsqlDataReader row) =>
        new(row.GetGuid(0), row.GetString(1), row.GetString(2),
            row.IsDBNull(3) ? null : row.GetString(3),
            row.GetString(4), row.GetInt32(5),
            JsonSerializer.Deserialize<JsonElement>(row.GetString(6)),
            row.GetDateTime(7), row.GetDateTime(8));
}
