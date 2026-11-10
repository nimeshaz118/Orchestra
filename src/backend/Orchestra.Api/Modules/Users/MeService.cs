using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Users;

public sealed class MeService(OrchestraDbContext db)
{
    public async Task<AppUser> UpdateAsync(Guid userId, UpdateMeRequest request, CancellationToken ct)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(
            u => u.UserId == userId && u.DeletedAt == null && u.Status == "ACTIVE", ct)
            ?? throw new ApiException(401, "INVALID_SESSION", "Please sign in again.");

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
            user.PhoneNumber = CleanOptional(request.PhoneNumber, 30);
        if (request.JobTitle is not null)
            user.JobTitle = CleanOptional(request.JobTitle, 120);

        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static string? CleanOptional(string value, int max)
    {
        var result = value.Trim();
        if (result.Length > max)
            throw new ApiException(422, "INVALID_PROFILE_FIELD", "One or more profile fields are too long.");
        return result.Length == 0 ? null : result;
    }

    private static string CleanName(string value, int max)
    {
        var result = value.Trim();
        if (result.Length is 0 || result.Length > max)
            throw new ApiException(422, "INVALID_NAME", "Enter a valid first and last name.");
        return result;
    }
}
