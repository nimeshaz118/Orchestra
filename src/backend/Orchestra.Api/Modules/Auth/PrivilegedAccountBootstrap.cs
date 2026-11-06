using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Auth;

public sealed class PrivilegedAccountBootstrap(IServiceProvider services,
    IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        foreach (var (role, key) in new[] { ("SUPER_ADMIN", "SuperAdmin"), ("ADMIN", "Admin") })
        {
            var email = configuration[$"Bootstrap:{key}:Email"]?.Trim().ToLowerInvariant();
            var password = configuration[$"Bootstrap:{key}:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) continue;
            if (password.Length < 12) throw new InvalidOperationException("Bootstrap passwords must be at least 12 characters.");
            if (await db.AppUsers.AnyAsync(u => u.RoleCode == role && u.Status == "ACTIVE" && u.DeletedAt == null, ct))
                continue;
            if (await db.AppUsers.AnyAsync(u => u.Email.ToLower() == email, ct))
                throw new InvalidOperationException($"The configured {key} bootstrap email is already registered.");
            var now = DateTime.UtcNow;
            var user = new AppUser
            {
                UserId = Guid.NewGuid(), Email = email, RoleCode = role,
                FirstName = key == "Admin" ? "Initial" : "Super",
                LastName = "Administrator", Status = "ACTIVE", EmailVerifiedAt = now,
                MustChangePassword = true, SecurityStamp = Guid.NewGuid(),
                CreatedAt = now, UpdatedAt = now
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
