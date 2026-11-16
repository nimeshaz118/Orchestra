using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Data;

public partial class OrchestraDbContext
{

    #pragma warning disable CA1822 // EF Core generated partial-method signature must match.

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Partial unique indexes limit privileged accounts, but ordinary users share USER.
        // Npgsql scaffolds the role FK as one-to-one; correct it without editing generated files.
        modelBuilder.Entity<Role>().Ignore(role => role.AppUser);
        modelBuilder.Entity<AppUser>().HasOne(user => user.RoleCodeNavigation)
            .WithMany().HasForeignKey(user => user.RoleCode)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("app_user_role_code_fkey");
    }
    #pragma warning restore CA1822
}
