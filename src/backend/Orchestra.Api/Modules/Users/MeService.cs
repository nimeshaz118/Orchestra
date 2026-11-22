using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;

namespace Orchestra.Api.Modules.Users;

public sealed class MeService(OrchestraDbContext db, IMapper mapper)
{
    public async Task<MeDto> GetAsync(Guid userId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync("CALL ai_orchestration.api_get_profile($1, 'api_rows')",
                row => new MeDto(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetString(3),
                    row.IsDBNull(4) ? null : row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5),
                    row.IsDBNull(6) ? null : row.GetString(6), row.GetString(7),
                    row.IsDBNull(8) ? null : row.GetGuid(8), row.GetString(9), row.GetBoolean(10),
                    row.GetDateTime(11), row.GetDateTime(12)), ct, userId);
            return rows.SingleOrDefault() ?? throw InvalidSession();
        }

        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(
            u => u.UserId == userId && u.DeletedAt == null && u.Status == "ACTIVE", ct);
        return mapper.Map<MeDto>(user ?? throw InvalidSession());
    }

    public async Task<MeDto> UpdateAsync(Guid userId, UpdateMeRequest request, CancellationToken ct)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(
            u => u.UserId == userId && u.DeletedAt == null && u.Status == "ACTIVE", ct)
            ?? throw InvalidSession();

        if (request.FirstName is null && request.LastName is null && request.DisplayName is null &&
            request.PhoneNumber is null && request.JobTitle is null)
            throw new ApiException(422, "NO_PROFILE_FIELDS", "Provide at least one profile field to update.");
        
        if (request.FirstName is not null)
            user.FirstName = CleanName(request.FirstName, 100);
        if (request.LastName is not null)
            user.LastName = CleanName(request.LastName, 100);
        if (request.DisplayName is not null)
            user.DisplayName = CleanOptional(request.DisplayName, 150);
        if (request.PhoneNumber is not null)
            user.PhoneNumber = CleanPhoneNumber(request.PhoneNumber);
        if (request.JobTitle is not null)
            user.JobTitle = CleanOptional(request.JobTitle, 120);

        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return mapper.Map<MeDto>(user);
    }

    private static ApiException InvalidSession() => new(401, "INVALID_SESSION", "Please sign in again.");

    private static string? CleanOptional(string value, int max)
    {
        var result = value.Trim();
        if (result.Length > max)
            throw new ApiException(422, "INVALID_PROFILE_FIELD", "One or more profile fields are too long.");
        return result.Length == 0 ? null : result;
    }

    private static string? CleanPhoneNumber(string value)
    {
        var result = value.Trim();
        if (result.Length == 0)
            return null;
        if (result.Length != 10 || result.Any(character => character is < '0' or > '9'))
            throw new ApiException(422, "INVALID_PHONE_NUMBER",
                "Please enter a valid phone number.");
        return result;
    }

    private static string CleanName(string value, int max)
    {
        var result = value.Trim();
        if (result.Length is 0 || result.Length > max)
            throw new ApiException(422, "INVALID_NAME", "Enter a valid first and last name.");
        return result;
    }
}
