using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Users;

public sealed class AvatarService(OrchestraDbContext db)
{
    public async Task SaveAsync(Guid userId, AvatarUpload? file, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        // Store the PNG directly in the user's bytea column.
        user.AvatarData = await AvatarValidator.ValidateAsync(file, ct);
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<byte[]> ReadAsync(Guid userId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return (await RequireActiveUserAsync(userId, ct)).AvatarData ?? throw NotFound();

        var rows = await db.ReadProcedureAsync("CALL ai_orchestration.api_get_avatar($1, 'api_rows')",
            row => row.IsDBNull(0) ? null : row.GetFieldValue<byte[]>(0), ct, userId);
        if (rows.Count == 0)
            throw InvalidSession();
        return rows[0] ?? throw NotFound();
    }

    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        if (user.AvatarData is null) throw NotFound();
        user.AvatarData = null; // Remove the image, keeping the user account.
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AppUser> RequireActiveUserAsync(Guid userId, CancellationToken ct) =>
        await db.AppUsers.SingleOrDefaultAsync(u => u.UserId == userId && u.Status == "ACTIVE" &&
            u.DeletedAt == null && u.EmailVerifiedAt != null, ct)
        ?? throw InvalidSession();

    private static ApiException InvalidSession() => new(401, "INVALID_SESSION", "Please sign in again.");
    private static ApiException NotFound() => new(404, "AVATAR_NOT_FOUND", "No avatar was found.");
}
