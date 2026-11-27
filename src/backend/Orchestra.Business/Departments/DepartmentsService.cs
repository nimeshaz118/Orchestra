using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestra.Api.Modules.Departments;

public sealed record DepartmentDto(Guid Id, string Name, string? Description, string Status);
public sealed record DepartmentModelEligibilityDto(
    Guid DepartmentId, string DepartmentName, Guid ModelId, string ModelName, bool IsEnabled);
public sealed record CreateDepartmentRequest(string Name, string? Description);

public sealed record UpdateDepartmentRequest
{
    public JsonElement Name { get; init; }
    public JsonElement Description { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class DepartmentsService(OrchestraDbContext db, IMapper mapper)
{
    public async Task<DepartmentModelEligibilityDto> EnableModelAsync(
        Guid departmentId, Guid modelId, Guid actor, CancellationToken ct)
    {
        var department = await GetDepartmentAsync(departmentId, ct);
        var model = await db.AiModels.SingleOrDefaultAsync(m => m.ModelId == modelId, ct)
            ?? throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");

        if (model.Status != "ACTIVE")
            throw new ApiException(409, "MODEL_NOT_ACTIVE", "Only active models can be enabled for a department.");

        var eligibility = await db.DepartmentModels.SingleOrDefaultAsync(
            dm => dm.DepartmentId == departmentId && dm.ModelId == modelId, ct);

        if (eligibility is null)
        {
            var now = DateTime.UtcNow;
            eligibility = new DepartmentModel
            {
                DepartmentId = departmentId,
                ModelId = modelId,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = actor
            };
            db.DepartmentModels.Add(eligibility);
            await db.SaveChangesAsync(ct);
        }
        else if (!eligibility.IsEnabled)
        {
            eligibility.IsEnabled = true;
            eligibility.UpdatedAt = DateTime.UtcNow;
            eligibility.UpdatedByUserId = actor;
            await db.SaveChangesAsync(ct);
        }

        return new DepartmentModelEligibilityDto(
            departmentId, department.DepartmentName, modelId, model.DisplayName, true);
    }

    public async Task<DepartmentModelEligibilityDto> DisableModelAsync(
        Guid departmentId, Guid modelId, Guid actor, CancellationToken ct)
    {
        var department = await GetDepartmentAsync(departmentId, ct);
        var model = await db.AiModels.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ModelId == modelId, ct)
            ?? throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");

        var eligibility = await db.DepartmentModels.SingleOrDefaultAsync(
            dm => dm.DepartmentId == departmentId && dm.ModelId == modelId, ct);
        if (eligibility is not null && eligibility.IsEnabled)
        {
            eligibility.IsEnabled = false;
            eligibility.UpdatedAt = DateTime.UtcNow;
            eligibility.UpdatedByUserId = actor;
            await db.SaveChangesAsync(ct);
        }

        return new DepartmentModelEligibilityDto(
            departmentId, department.DepartmentName, modelId, model.DisplayName, false);
    }

    private async Task<Department> GetDepartmentAsync(Guid departmentId, CancellationToken ct) =>
        await db.Departments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.DepartmentId == departmentId, ct)
        ?? throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

    public Task<List<DepartmentDto>> PublicListAsync(CancellationToken ct) => ListAsync(true, ct);

    public Task<List<DepartmentDto>> AdminListAsync(CancellationToken ct) => ListAsync(false, ct);

    private Task<List<DepartmentDto>> ListAsync(bool activeOnly, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? db.ReadProcedureAsync("CALL ai_orchestration.api_list_departments($1, 'api_rows')",
                ReadDepartment, ct, activeOnly)
            : db.Departments.AsNoTracking().Where(d => !activeOnly || d.Status == "ACTIVE")
                .OrderBy(d => d.DepartmentName)
                .ProjectTo<DepartmentDto>(mapper.ConfigurationProvider)
                .ToListAsync(ct);

    public async Task<DepartmentDto> GetByIdAsync(Guid departmentId, CancellationToken ct)
    {
        DepartmentDto? department;
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync("CALL ai_orchestration.api_get_department($1, 'api_rows')",
                ReadDepartment, ct, departmentId);
            department = rows.SingleOrDefault();
        }
        else
        {
            department = await db.Departments.AsNoTracking().Where(d => d.DepartmentId == departmentId)
                .ProjectTo<DepartmentDto>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(ct);
        }

        return department ?? throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");
    }

    private static DepartmentDto ReadDepartment(NpgsqlDataReader row) =>
        new(row.GetGuid(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2), row.GetString(3));

    private static ApiException InvalidField(
        string code, string message, IReadOnlyDictionary<string, string[]> errors)
        => new(422, code, message, errors);

    private static ApiException InvalidField(string field, string fieldMessage)
        => InvalidField(
            "INVALID_DEPARTMENT_UPDATE",
            "One or more department fields are invalid.",
            new Dictionary<string, string[]> { [field] = [fieldMessage] });

    public Task<DepartmentDto> ActivateAsync(Guid departmentId, Guid actor, CancellationToken ct)
        => SetStatusAsync(departmentId, "ACTIVE", actor, ct);

    public Task<DepartmentDto> DeactivateAsync(Guid departmentId, Guid actor, CancellationToken ct)
        => SetStatusAsync(departmentId, "INACTIVE", actor, ct);

    //To fix sonarqube errors.
    private static readonly string[] NonEditableFieldError =
    ["This field cannot be changed."];

    private async Task<DepartmentDto> SetStatusAsync(
        Guid departmentId, string status, Guid actor, CancellationToken ct)
    {
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.DepartmentId == departmentId, ct)
            ?? throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        if (department.Status == status)
            return mapper.Map<DepartmentDto>(department);

        department.Status = status;
        department.UpdatedAt = DateTime.UtcNow;
        department.UpdatedByUserId = actor;
        await db.SaveChangesAsync(ct);

        return mapper.Map<DepartmentDto>(department);
    }

    public async Task<DepartmentDto> UpdateAsync(
        Guid departmentId, UpdateDepartmentRequest request, Guid actor, CancellationToken ct)
    {
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.DepartmentId == departmentId, ct)
            ?? throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        if (request.AdditionalProperties is { Count: > 0 } additionalProperties)
        {
            var errors = additionalProperties.Keys.ToDictionary(
                field => field,
                _ => NonEditableFieldError);
            throw InvalidField(
                "INVALID_DEPARTMENT_UPDATE",
                "One or more department fields are not editable.",
                errors);
        }

        var updateName = request.Name.ValueKind != JsonValueKind.Undefined;
        var updateDescription = request.Description.ValueKind != JsonValueKind.Undefined;
        if (!updateName && !updateDescription)
            throw InvalidField("request", "Provide at least one department field to update.");

        string? name = null;
        if (updateName)
        {
            if (request.Name.ValueKind != JsonValueKind.String)
                throw InvalidField("name", "Department name must be a string.");

            name = request.Name.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw InvalidField("name", "Department name is required.");
            if (name.Length > 150)
                throw InvalidField("name", "Department name cannot exceed 150 characters.");
        }

        string? description = null;
        if (updateDescription)
        {
            if (request.Description.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw InvalidField("description", "Description must be a string or null.");

            description = request.Description.ValueKind == JsonValueKind.Null
                ? null
                : request.Description.GetString()?.Trim();
            if (description?.Length > 2000)
                throw InvalidField("description", "Description cannot exceed 2000 characters.");
        }

        if (name is not null)
        { 
            var normalized = name.ToLowerInvariant();

            // EF Core translates ToLower() into SQL LOWER().
            // StringComparison.OrdinalIgnoreCase is not translated.
            #pragma warning disable CA1862
            if (await db.Departments.AnyAsync(
                    d => d.DepartmentId != departmentId &&
                        d.DepartmentName.ToLower() == normalized, ct))
                throw DuplicateName();
            #pragma warning restore CA1862

            department.DepartmentName = name;
        }

        if (updateDescription)
            department.Description = description;

        department.UpdatedAt = DateTime.UtcNow;
        department.UpdatedByUserId = actor;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Covers a duplicate-name race between the check above and the database write.
            throw DuplicateName();
        }

        return mapper.Map<DepartmentDto>(department);
    }

    private static ApiException DuplicateName()
        => new(
            409,
            "DEPARTMENT_EXISTS",
            "A department with this name already exists.",
            new Dictionary<string, string[]> { ["name"] = ["Choose a different department name."] });

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, Guid actor, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw InvalidField(
                "INVALID_DEPARTMENT",
                "One or more department fields are invalid.",
                new Dictionary<string, string[]> { ["name"] = ["Department name is required."] });

        var validationErrors = new Dictionary<string, string[]>();
        if (name.Length > 150)
            validationErrors["name"] = ["Department name cannot exceed 150 characters."];
        if (request.Description?.Length > 2000)
            validationErrors["description"] = ["Description cannot exceed 2000 characters."];
        if (validationErrors.Count > 0)
            throw InvalidField(
                "INVALID_DEPARTMENT",
                "One or more department fields are invalid.",
                validationErrors);

        var normalized = name.ToLowerInvariant();

        // Keep the SQL-compatible comparison for EF Core.
        #pragma warning disable CA1862
        if (await db.Departments.AnyAsync(d => d.DepartmentName.ToLower() == normalized, ct))
            throw DuplicateName();
        #pragma warning restore CA1862
        
        var now = DateTime.UtcNow;
        var department = new Department
        {
            DepartmentId = Guid.NewGuid(),
            DepartmentName = name,
            Description = request.Description?.Trim(),
            Status = "ACTIVE",
            Configuration = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = actor
        };
        db.Departments.Add(department);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw DuplicateName();
        }
        return mapper.Map<DepartmentDto>(department);
    }
}
