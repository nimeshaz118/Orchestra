using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Departments;

public sealed record DepartmentDto(Guid Id, string Name, string? Description, string Status);
public sealed record CreateDepartmentRequest(string Name, string? Description);

public sealed class DepartmentsService(OrchestraDbContext db)
{
    public Task<List<DepartmentDto>> PublicListAsync(CancellationToken ct) => db.Departments.AsNoTracking()
        .Where(d => d.Status == "ACTIVE").OrderBy(d => d.DepartmentName)
        .Select(d => new DepartmentDto(d.DepartmentId, d.DepartmentName, d.Description, d.Status))
        .ToListAsync(ct);

    public Task<List<DepartmentDto>> AdminListAsync(CancellationToken ct) => db.Departments.AsNoTracking()
        .OrderBy(d => d.DepartmentName)
        .Select(d => new DepartmentDto(d.DepartmentId, d.DepartmentName, d.Description, d.Status))
        .ToListAsync(ct);

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, Guid actor, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || request.Description?.Length > 2000)
            throw new ApiException(422, "INVALID_DEPARTMENT", "Enter a valid department name and description.");
        var normalized = name.ToLowerInvariant();
        if (await db.Departments.AnyAsync(d => d.DepartmentName.ToLower() == normalized, ct))
            throw new ApiException(409, "DEPARTMENT_EXISTS", "The department already exists.");
        var now = DateTime.UtcNow;
        var department = new Department
        {
            DepartmentId = Guid.NewGuid(), DepartmentName = name, Description = request.Description?.Trim(),
            Status = "ACTIVE", Configuration = "{}", CreatedAt = now, UpdatedAt = now,
            CreatedByUserId = actor
        };
        db.Departments.Add(department);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApiException(409, "DEPARTMENT_EXISTS", "The department already exists.");
        }
        return new DepartmentDto(department.DepartmentId, department.DepartmentName,
            department.Description, department.Status);
    }
}
