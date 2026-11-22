using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.PlatformSettings;

/// <summary>Payload for creating a platform setting.</summary>
public sealed record CreatePlatformSettingRequest(
    string? Key,
    JsonElement Value,
    string? Description,
    bool IsSensitive);

/// <summary>Payload for updating a platform setting.</summary>
public sealed record UpdatePlatformSettingRequest
{
    public string? Key { get; init; }
    public JsonElement Value { get; init; }
    public JsonElement Description { get; init; }
    public JsonElement IsSensitive { get; init; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Request to apply or revoke a platform setting.</summary>
public sealed record ApplyPlatformSettingRequest(string? Key, string? Action);

/// <summary>A platform setting and its current apply state.</summary>
public sealed record PlatformSettingDto(
    string Key,
    JsonElement? Value,
    string? Description,
    bool IsSensitive,
    bool IsApplied);

public sealed class PlatformSettingsService(OrchestraDbContext db)
{
    public async Task<PlatformSettingDto> CreateAsync(
        CreatePlatformSettingRequest request, Guid actor, CancellationToken ct)
    {
        var key = ValidateKey(request.Key);
        if (request.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw InvalidField("value", "A non-null JSON value is required.");

        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_create_platform_setting(" +
                "$1, $2::jsonb, $3::text, $4, $5, 'api_rows')",
                ReadSetting,
                ct,
                key,
                request.Value.GetRawText(),
                (object?)request.Description ?? DBNull.Value,
                request.IsSensitive,
                actor);

            return rows.SingleOrDefault() ?? throw SettingAlreadyExists();
        }

        if (await db.PlatformSettings.AnyAsync(setting => setting.SettingKey == key, ct))
            throw SettingAlreadyExists();

        var now = DateTime.UtcNow;
        var setting = new PlatformSetting
        {
            SettingKey = key,
            SettingValue = request.Value.GetRawText(),
            Description = request.Description,
            IsSensitive = request.IsSensitive,
            IsApplied = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = actor
        };
        db.PlatformSettings.Add(setting);
        await db.SaveChangesAsync(ct);
        return ToDto(setting);
    }

    public async Task<PlatformSettingDto> UpdateAsync(
        UpdatePlatformSettingRequest request, Guid actor, CancellationToken ct)
    {
        var key = ValidateKey(request.Key);

        if (request.AdditionalProperties is { Count: > 0 } additionalProperties)
        {
            var errors = additionalProperties.Keys.ToDictionary(
                field => field,
                _ => new[] { "This field cannot be changed." });
            throw InvalidFields(
                "One or more platform setting fields are not editable.",
                errors);
        }

        var updateValue = request.Value.ValueKind != JsonValueKind.Undefined;
        var updateDescription = request.Description.ValueKind != JsonValueKind.Undefined;
        var updateSensitivity = request.IsSensitive.ValueKind != JsonValueKind.Undefined;
        if (!updateValue && !updateDescription && !updateSensitivity)
            throw InvalidField("request", "Provide at least one setting field to update.");

        if (updateValue && request.Value.ValueKind == JsonValueKind.Null)
            throw InvalidField("value", "A non-null JSON value is required.");
        if (updateDescription &&
            request.Description.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw InvalidField("description", "Description must be a string or null.");
        if (updateSensitivity &&
            request.IsSensitive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidField("isSensitive", "Sensitivity must be true or false.");

        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_update_platform_setting(" +
                "$1, $2::jsonb, $3, $4::text, $5, $6, $7, $8, 'api_rows')",
                ReadSetting,
                ct,
                key,
                updateValue ? request.Value.GetRawText() : "null",
                updateValue,
                updateDescription && request.Description.ValueKind == JsonValueKind.String
                    ? (object)request.Description.GetString()!.Trim()
                    : DBNull.Value,
                updateDescription,
                updateSensitivity && request.IsSensitive.GetBoolean(),
                updateSensitivity,
                actor);

            return rows.SingleOrDefault() ?? throw SettingNotFound();
        }

        var setting = await db.PlatformSettings
            .SingleOrDefaultAsync(item => item.SettingKey == key, ct)
            ?? throw SettingNotFound();

        if (updateValue)
            setting.SettingValue = request.Value.GetRawText();
        if (updateDescription)
            setting.Description = request.Description.ValueKind == JsonValueKind.Null
                ? null
                : request.Description.GetString()?.Trim();
        if (updateSensitivity)
            setting.IsSensitive = request.IsSensitive.GetBoolean();

        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actor;
        await db.SaveChangesAsync(ct);
        return ToDto(setting);
    }

    public async Task<PlatformSettingDto> SetAppliedAsync(
        ApplyPlatformSettingRequest request, Guid actor, CancellationToken ct)
    {
        var key = ValidateKey(request.Key);
        var action = request.Action?.Trim().ToUpperInvariant();
        var isApplied = action switch
        {
            "APPLY" => true,
            "REVOKE" => false,
            _ => throw InvalidField("action", "Action must be APPLY or REVOKE.")
        };

        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_set_platform_setting_applied(" +
                "$1, $2, $3, 'api_rows')",
                ReadSetting,
                ct,
                key,
                isApplied,
                actor);

            return rows.SingleOrDefault() ?? throw SettingNotFound();
        }

        var setting = await db.PlatformSettings
            .SingleOrDefaultAsync(item => item.SettingKey == key, ct)
            ?? throw SettingNotFound();

        setting.IsApplied = isApplied;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actor;
        await db.SaveChangesAsync(ct);
        return ToDto(setting);
    }

    public async Task<List<PlatformSettingDto>> GetAllAsync(CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_platform_settings('api_rows')",
                ReadSetting,
                ct);
        }

        var settings = await db.PlatformSettings.AsNoTracking()
            .OrderBy(setting => setting.SettingKey)
            .ToListAsync(ct);
        return settings.Select(ToDto).ToList();
    }

    private static string ValidateKey(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw InvalidField("key", "Setting key is required.");
        if (key.Length > 120)
            throw InvalidField("key", "Setting key cannot exceed 120 characters.");
        return key;
    }

    private static PlatformSettingDto ToDto(PlatformSetting setting)
        => new(
            setting.SettingKey,
            setting.IsSensitive ? null : ParseValue(setting.SettingValue),
            setting.Description,
            setting.IsSensitive,
            setting.IsApplied);

    private static PlatformSettingDto ReadSetting(NpgsqlDataReader row)
    {
        var isSensitive = row.GetBoolean(3);
        return new PlatformSettingDto(
            row.GetString(0),
            isSensitive ? null : ParseValue(row.GetString(1)),
            row.IsDBNull(2) ? null : row.GetString(2),
            isSensitive,
            row.GetBoolean(4));
    }

    private static JsonElement ParseValue(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static ApiException InvalidField(string field, string message)
        => new(
            422,
            "INVALID_PLATFORM_SETTING",
            "One or more platform setting fields are invalid.",
            new Dictionary<string, string[]> { [field] = [message] });

    private static ApiException InvalidFields(
        string message, IReadOnlyDictionary<string, string[]> errors)
        => new(422, "INVALID_PLATFORM_SETTING", message, errors);

    private static ApiException SettingAlreadyExists()
        => new(
            409,
            "PLATFORM_SETTING_EXISTS",
            "A platform setting with this key already exists.",
            new Dictionary<string, string[]> { ["key"] = ["Choose a different setting key."] });

    private static ApiException SettingNotFound()
        => new(404, "PLATFORM_SETTING_NOT_FOUND", "The platform setting was not found.");
}
