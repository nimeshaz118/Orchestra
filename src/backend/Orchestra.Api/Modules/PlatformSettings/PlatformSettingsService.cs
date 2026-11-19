using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.PlatformSettings;

public sealed record CreatePlatformSettingRequest(
    string? Key,
    JsonElement Value,
    string? Description,
    bool IsSensitive);

public sealed record UpdatePlatformSettingRequest
{
    public string? Key { get; init; }
    public JsonElement Value { get; init; }
    public JsonElement Description { get; init; }
    public JsonElement IsSensitive { get; init; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record EffectivePlatformSettingDto(
    string Key,
    JsonElement? Value,
    string? Description,
    bool IsSensitive);

public sealed class PlatformSettingsService(OrchestraDbContext db)
{
    public async Task<EffectivePlatformSettingDto> UpdateAsync(
        UpdatePlatformSettingRequest request, Guid actor, CancellationToken ct)
    {
        var key = request.Key?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw InvalidField("key", "Setting key is required.");
        if (key.Length > 120)
            throw InvalidField("key", "Setting key cannot exceed 120 characters.");

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

        var setting = await db.PlatformSettings
            .FirstOrDefaultAsync(item => item.SettingKey == key, ct)
            ?? throw new ApiException(
                404,
                "PLATFORM_SETTING_NOT_FOUND",
                "The platform setting was not found.");

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

    public async Task<EffectivePlatformSettingDto> CreateAsync(
        CreatePlatformSettingRequest request, Guid actor, CancellationToken ct)
    {
        var key = request.Key?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw InvalidField("key", "Setting key is required.");
        if (key.Length > 120)
            throw InvalidField("key", "Setting key cannot exceed 120 characters.");
        if (request.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw InvalidField("value", "A non-null JSON value is required.");

        if (await db.PlatformSettings.AnyAsync(setting => setting.SettingKey == key, ct))
            throw SettingAlreadyExists();

        var now = DateTime.UtcNow;
        var setting = new PlatformSetting
        {
            SettingKey = key,
            SettingValue = request.Value.GetRawText(),
            Description = request.Description,
            IsSensitive = request.IsSensitive,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = actor
        };
        db.PlatformSettings.Add(setting);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw SettingAlreadyExists();
        }

        return ToDto(setting);
    }

    public async Task<List<EffectivePlatformSettingDto>> GetEffectiveAsync(CancellationToken ct)
    {
        var settings = await db.PlatformSettings.AsNoTracking()
            .OrderBy(setting => setting.SettingKey)
            .Select(setting => new
            {
                setting.SettingKey,
                setting.SettingValue,
                setting.Description,
                setting.IsSensitive
            })
            .ToListAsync(ct);

        return settings.Select(setting => new EffectivePlatformSettingDto(
            setting.SettingKey,
            setting.IsSensitive ? null : ParseValue(setting.SettingValue),
            setting.Description,
            setting.IsSensitive)).ToList();
    }

    private static EffectivePlatformSettingDto ToDto(PlatformSetting setting)
        => new(
            setting.SettingKey,
            setting.IsSensitive ? null : ParseValue(setting.SettingValue),
            setting.Description,
            setting.IsSensitive);

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
}
