using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using UserPreferenceEntity = Orchestra.Api.Models.Entities.UserPreference;

namespace Orchestra.Api.Controller.UserPreference;

public sealed class UserPreferenceService(OrchestraDbContext db)
{
    private const int MaxValueLength = 16_384;

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetAsync(
        Guid userId, CancellationToken ct)
    {
        var preferences = await db.UserPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToListAsync(ct);

        return preferences.ToDictionary(
            preference => preference.PreferenceKey,
            preference => ParseValue(preference.PreferenceValue));
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> UpdateAsync(
        Guid userId, Dictionary<string, JsonElement> request, CancellationToken ct)
    {
        var updates = NormalizeAndValidate(request);
        var existing = await db.UserPreferences
            .Where(preference => preference.UserId == userId &&
                updates.Keys.Contains(preference.PreferenceKey))
            .ToDictionaryAsync(preference => preference.PreferenceKey, ct);
        var now = DateTime.UtcNow;

        foreach (var (key, value) in updates)
        {
            if (existing.TryGetValue(key, out var preference))
            {
                preference.PreferenceValue = value.GetRawText();
                preference.UpdatedAt = now;
                continue;
            }

            db.UserPreferences.Add(new UserPreferenceEntity
            {
                PreferenceId = Guid.NewGuid(),
                UserId = userId,
                PreferenceKey = key,
                PreferenceValue = value.GetRawText(),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    private static Dictionary<string, JsonElement> NormalizeAndValidate(
        Dictionary<string, JsonElement> request)
    {
        var updates = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (rawKey, value) in request)
        {
            var key = rawKey.Trim();
            if (key.Length == 0 || key.Length > 100 || value.GetRawText().Length > MaxValueLength)
                throw new ApiException(422, "INVALID_PREFERENCES",
                    "Preference keys and values exceed the allowed limits.");
            if (!updates.TryAdd(key, value))
                throw new ApiException(422, "INVALID_PREFERENCES",
                    "Preference keys must be unique.");
        }

        return updates;
    }

    private static JsonElement ParseValue(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
