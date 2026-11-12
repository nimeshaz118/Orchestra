using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Domains;

public sealed record DomainDto(
    Guid DomainId,
    string DomainName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Nullable so our service can give a specific message when the field is missing.
public sealed record CreateDomainRequest(string? DomainName);

public sealed record CreateDomainResult(DomainDto Domain, bool Created);

public sealed partial class DomainsService(OrchestraDbContext db)
{
    // Regex implementation generated at compile time
    [GeneratedRegex(
        @"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();


    public Task<List<DomainDto>> ListAsync(CancellationToken ct) =>
        db.ApprovedEmailDomains
            .AsNoTracking()
            .OrderBy(d => d.DomainName)
            .Select(d => new DomainDto(
                d.DomainId,
                d.DomainName,
                d.IsActive,
                d.CreatedAt,
                d.UpdatedAt))
            .ToListAsync(ct);

    public async Task<CreateDomainResult> CreateAsync(
        CreateDomainRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        var name = request.DomainName?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException(
                422,
                "DOMAIN_REQUIRED",
                "Enter an email domain, such as university.edu.");

        if (!DomainPattern().IsMatch(name))
            throw new ApiException(
                422,
                "INVALID_DOMAIN",
                "Enter a valid domain such as university.edu. Do not include @, https://, spaces, or a wildcard.");

        // Preserve PostgreSQL-compatible case-insensitive comparison.
        #pragma warning disable CA1862
        var existing = await db.ApprovedEmailDomains
            .FirstOrDefaultAsync(d => d.DomainName.ToLower() == name, ct);
        #pragma warning restore CA1862

        if (existing is not null)
        {
            if (existing.IsActive)
                throw new ApiException(
                    409,
                    "DOMAIN_ALREADY_APPROVED",
                    "This email domain is already approved.");

            // Reactivate the existing row after a previous soft delete.
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = actorId;

            await db.SaveChangesAsync(ct);
            return new CreateDomainResult(ToDto(existing), Created: false);
        }

        var now = DateTime.UtcNow;
        var domain = new ApprovedEmailDomain
        {
            DomainId = Guid.NewGuid(),
            DomainName = name,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = actorId
        };

        db.ApprovedEmailDomains.Add(domain);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_approved_email_domain_ci"
            })
        {
            // Handles two administrators submitting the same domain simultaneously.
            throw new ApiException(
                409,
                "DOMAIN_ALREADY_APPROVED",
                "This email domain already exists.");
        }

        return new CreateDomainResult(ToDto(domain), Created: true);
    }


    //Domain delete part
    /*
    public async Task DeleteAsync(
        Guid domainId,
        Guid actorId,
        CancellationToken ct)
    {
        if (domainId == Guid.Empty)
            throw new ApiException(
                422,
                "INVALID_DOMAIN_ID",
                "Enter a valid domain ID.");

        var domain = await db.ApprovedEmailDomains
            .FirstOrDefaultAsync(d => d.DomainId == domainId, ct);

        if (domain is null)
            throw new ApiException(
                404,
                "DOMAIN_NOT_FOUND",
                "The email domain was not found.");

        // An already inactive domain is still deleted from the user's perspective.
        if (!domain.IsActive)
            return;

        // Soft delete: keep the database row and mark it inactive.
        domain.IsActive = false;
        domain.UpdatedAt = DateTime.UtcNow;
        domain.UpdatedByUserId = actorId;

        await db.SaveChangesAsync(ct);
    }
    */

    private static DomainDto ToDto(ApprovedEmailDomain domain) =>
        new(
            domain.DomainId,
            domain.DomainName,
            domain.IsActive,
            domain.CreatedAt,
            domain.UpdatedAt);
}