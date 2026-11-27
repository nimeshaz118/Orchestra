using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.RoutingPolicies.DTOs;
using System.Text.Json;

namespace Orchestra.Api.Modules.RoutingPolicies;

public sealed class RoutingPoliciesService(OrchestraDbContext db, IMapper mapper)
{
    private static readonly string[] Categories = ["GENERAL_PURPOSE", "CODING"];
    private static readonly string[] ScopeTypes = ["GLOBAL", "DEPARTMENT"];

    public Task<List<RoutingPolicyDto>> ListAsync(CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_routing_policies('api_rows')",
                ReadPolicy, ct)
            : db.Set<RoutingPolicy>()
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.PolicyName)
                .ProjectTo<RoutingPolicyDto>(mapper.ConfigurationProvider)
                .ToListAsync(ct);

    public async Task<List<RoutingPolicyDto>> ListByDepartmentAsync(
        Guid departmentId,
        CancellationToken ct)
    {
        if (departmentId == Guid.Empty)
            throw new ApiException(400, "INVALID_DEPARTMENT_ID", "A valid department ID is required.");

        if (!await db.Departments.AsNoTracking().AnyAsync(d => d.DepartmentId == departmentId, ct))
            throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        var policies = db.Database.IsNpgsql()
            ? await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_routing_policies_by_department($1, 'api_rows')",
                ReadPolicy, ct, departmentId)
            : await db.Set<RoutingPolicy>()
                .AsNoTracking()
                .Where(p => p.DepartmentId == departmentId && p.IsActive)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.PolicyName)
                .ProjectTo<RoutingPolicyDto>(mapper.ConfigurationProvider)
                .ToListAsync(ct);

        return policies.Count == 0
            ? throw new ApiException(
                404,
                "ROUTING_POLICIES_NOT_FOUND",
                "No routing policies were found for the department.")
            : policies;
    }

    public async Task<List<RoutingPolicyDto>> ListDeletedByScopeAsync(
        string scopeType,
        CancellationToken ct)
    {
        scopeType = scopeType.Trim().ToUpperInvariant();
        if (!ScopeTypes.Contains(scopeType, StringComparer.Ordinal))
            throw InvalidRequest(new Dictionary<string, string[]>
            {
                ["scopeType"] = ["Scope type must be GLOBAL or DEPARTMENT."]
            });

        if (db.Database.IsNpgsql())
            return await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_deleted_routing_policies_by_scope($1, 'api_rows')",
                ReadPolicy, ct, scopeType);

        return await db.Set<RoutingPolicy>()
            .AsNoTracking()
            .Where(p => !p.IsActive && p.ScopeType == scopeType)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.PolicyName)
            .ProjectTo<RoutingPolicyDto>(mapper.ConfigurationProvider)
            .ToListAsync(ct);
    }

    public async Task<List<RoutingPolicyDto>> ListDeletedByDepartmentAsync(
        Guid departmentId,
        CancellationToken ct)
    {
        if (departmentId == Guid.Empty)
            throw new ApiException(400, "INVALID_DEPARTMENT_ID", "A valid department ID is required.");
        if (!await db.Departments.AsNoTracking().AnyAsync(d => d.DepartmentId == departmentId, ct))
            throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        if (db.Database.IsNpgsql())
            return await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_deleted_routing_policies_by_department($1, 'api_rows')",
                ReadPolicy, ct, departmentId);

        return await db.Set<RoutingPolicy>()
            .AsNoTracking()
            .Where(p => !p.IsActive && p.DepartmentId == departmentId)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.PolicyName)
            .ProjectTo<RoutingPolicyDto>(mapper.ConfigurationProvider)
            .ToListAsync(ct);
    }

    public async Task<RoutingPolicyDto> GetByIdAsync(Guid policyId, CancellationToken ct)
    {
        if (policyId == Guid.Empty)
            throw new ApiException(400, "INVALID_ROUTING_POLICY_ID", "A valid routing policy ID is required.");

        RoutingPolicyDto? result;
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_routing_policy($1, 'api_rows')",
                ReadPolicy, ct, policyId);
            result = rows.SingleOrDefault();
        }
        else
        {
            var policy = await db.Set<RoutingPolicy>()
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.PolicyId == policyId && p.IsActive, ct);
            result = policy is null ? null : mapper.Map<RoutingPolicyDto>(policy);
        }

        return result ?? throw RoutingPolicyNotFound();
    }

    public async Task<RoutingPolicyDto> CreateAsync(
        CreateRoutingPolicyRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        var policyName = request.PolicyName?.Trim();
        var taskCategory = request.TaskCategory?.Trim().ToUpperInvariant();
        var scopeType = request.ScopeType?.Trim().ToUpperInvariant();
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(policyName))
            errors["policyName"] = ["Policy name is required."];
        else if (policyName.Length > 150)
            errors["policyName"] = ["Policy name cannot exceed 150 characters."];
        if (request.ModelId is null || request.ModelId == Guid.Empty)
            errors["modelId"] = ["A valid model ID is required."];
        if (!Categories.Contains(taskCategory, StringComparer.Ordinal))
            errors["taskCategory"] = ["Task category must be GENERAL_PURPOSE or CODING."];
        if (!ScopeTypes.Contains(scopeType, StringComparer.Ordinal))
            errors["scopeType"] = ["Scope type must be GLOBAL or DEPARTMENT."];
        if (request.Priority <= 0)
            errors["priority"] = ["Priority must be greater than zero."];
        if (scopeType == "GLOBAL" && request.DepartmentId is not null)
            errors["departmentId"] = ["Global policies cannot specify a department."];
        if (scopeType == "DEPARTMENT" && (request.DepartmentId is null || request.DepartmentId == Guid.Empty))
            errors["departmentId"] = ["A department is required for department policies."];
        if (request.Description?.Length > 2000)
            errors["description"] = ["Description cannot exceed 2000 characters."];

        if (errors.Count > 0)
            throw InvalidRequest(errors);

        var modelId = request.ModelId!.Value;
        var description = request.Description?.Trim();
        var model = await db.AiModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ModelId == modelId, ct);
        if (model is null)
            throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");
        if (!string.Equals(model.Status, "ACTIVE", StringComparison.Ordinal))
            throw new ApiException(422, "MODEL_DISABLED", "Disabled models cannot be used in routing policies.");

        if (request.DepartmentId is Guid departmentId &&
            !await db.Departments.AsNoTracking().AnyAsync(d => d.DepartmentId == departmentId, ct))
            throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        if (await HasActiveDefinitionAsync(
                modelId,
                taskCategory!,
                scopeType!,
                request.DepartmentId,
                excludedPolicyId: null,
                ct))
            throw RoutingPolicyAlreadyExists();

        var correlationId = Guid.NewGuid();
        if (db.Database.IsNpgsql())
        {
            try
            {
                var rows = await db.ReadProcedureAsync(
                    "CALL ai_orchestration.api_create_routing_policy($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, 'api_rows')",
                    ReadPolicy, ct, policyName!, (object?)description ?? DBNull.Value, modelId, taskCategory!,
                    scopeType!, (object?)request.DepartmentId ?? DBNull.Value, request.Priority, request.IsEnabled,
                    actorId, correlationId);
                return rows.Single();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new ApiException(422, "INVALID_ROUTING_POLICY_REFERENCE",
                    "A referenced routing policy record is invalid.");
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                ex.ConstraintName == "uq_routing_policy_active_definition")
            {
                throw RoutingPolicyAlreadyExists();
            }
        }

        var now = DateTime.UtcNow;
        var policy = new RoutingPolicy
        {
            PolicyId = Guid.NewGuid(),
            PolicyName = policyName!,
            Description = description,
            ModelId = modelId,
            TaskCategory = taskCategory!,
            ScopeType = scopeType!,
            DepartmentId = request.DepartmentId,
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        db.Set<RoutingPolicy>().Add(policy);
        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRoleCode = "SUPER_ADMIN",
            Action = "ROUTING_POLICY_CREATED",
            TargetType = "ROUTING_POLICY",
            TargetId = policy.PolicyId,
            DepartmentId = policy.DepartmentId,
            NewValues = JsonSerializer.Serialize(new
            {
                policy.PolicyName,
                policy.TaskCategory,
                policy.ScopeType,
                policy.ModelId,
                policy.Priority,
                policy.IsEnabled
            }),
            Outcome = "SUCCESS",
            CorrelationId = correlationId,
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return mapper.Map<RoutingPolicyDto>(policy);
    }

    public async Task<RoutingPolicyDto> PatchAsync(
        Guid policyId,
        PatchRoutingPolicyRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (policyId == Guid.Empty)
            throw new ApiException(400, "INVALID_ROUTING_POLICY_ID", "A valid routing policy ID is required.");

        if (request.PolicyName is null && request.Description is null && request.ModelId is null &&
            request.TaskCategory is null && request.ScopeType is null && request.DepartmentId is null &&
            request.Priority is null && request.IsEnabled is null)
            throw InvalidRequest(new Dictionary<string, string[]>
            {
                ["request"] = ["At least one routing policy field must be provided."]
            });

        var existing = await db.Set<RoutingPolicy>()
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.PolicyId == policyId && p.IsActive, ct)
            ?? throw RoutingPolicyNotFound();

        var policyName = request.PolicyName?.Trim() ?? existing.PolicyName;
        var description = request.Description?.Trim() ?? existing.Description;
        var modelId = request.ModelId ?? existing.ModelId;
        var taskCategory = request.TaskCategory?.Trim().ToUpperInvariant() ?? existing.TaskCategory;
        var scopeType = request.ScopeType?.Trim().ToUpperInvariant() ?? existing.ScopeType;
        var departmentId = scopeType == "GLOBAL"
            ? null
            : request.DepartmentId ?? existing.DepartmentId;
        var priority = request.Priority ?? existing.Priority;
        var isEnabled = request.IsEnabled ?? existing.IsEnabled;

        var errors = new Dictionary<string, string[]>();

        if (request.PolicyName is not null &&
            (string.IsNullOrWhiteSpace(policyName) || policyName.Length > 150))
            errors["policyName"] = ["Policy name must contain 1 to 150 characters."];
        if (request.Description?.Length > 2000)
            errors["description"] = ["Description cannot exceed 2000 characters."];
        if (request.ModelId == Guid.Empty)
            errors["modelId"] = ["A valid model ID is required."];
        if (request.TaskCategory is not null && !Categories.Contains(taskCategory, StringComparer.Ordinal))
            errors["taskCategory"] = ["Task category must be GENERAL_PURPOSE or CODING."];
        if (request.ScopeType is not null && !ScopeTypes.Contains(scopeType, StringComparer.Ordinal))
            errors["scopeType"] = ["Scope type must be GLOBAL or DEPARTMENT."];
        if (request.DepartmentId == Guid.Empty)
            errors["departmentId"] = ["A valid department ID is required."];
        if (scopeType == "GLOBAL" && request.DepartmentId is not null)
            errors["departmentId"] = ["Global policies cannot specify a department."];
        if (scopeType == "DEPARTMENT" && departmentId is null)
            errors["departmentId"] = ["A department is required for department policies."];
        if (request.Priority is <= 0)
            errors["priority"] = ["Priority must be greater than zero."];

        if (errors.Count > 0)
            throw InvalidRequest(errors);

        if (request.ModelId is not null)
        {
            var model = await db.AiModels.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ModelId == modelId, ct);
            if (model is null)
                throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");
            if (!string.Equals(model.Status, "ACTIVE", StringComparison.Ordinal))
                throw new ApiException(422, "MODEL_DISABLED", "Disabled models cannot be used in routing policies.");
        }

        if (scopeType == "DEPARTMENT" && departmentId is Guid targetDepartmentId &&
            !await db.Departments.AsNoTracking().AnyAsync(d => d.DepartmentId == targetDepartmentId, ct))
            throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        if (await HasActiveDefinitionAsync(
                modelId,
                taskCategory,
                scopeType,
                departmentId,
                policyId,
                ct))
            throw RoutingPolicyAlreadyExists();

        var updateDepartment = request.DepartmentId is not null ||
            (request.ScopeType is not null && scopeType == "GLOBAL");

        var correlationId = Guid.NewGuid();
        if (db.Database.IsNpgsql())
        {
            try
            {
                var rows = await db.ReadProcedureAsync(
                    "CALL ai_orchestration.api_patch_routing_policy($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, 'api_rows')",
                    ReadPolicy, ct, policyId, actorId, correlationId,
                    (object?)request.PolicyName?.Trim() ?? DBNull.Value,
                    (object?)request.Description?.Trim() ?? DBNull.Value,
                    (object?)request.ModelId ?? DBNull.Value,
                    (object?)request.TaskCategory?.Trim().ToUpperInvariant() ?? DBNull.Value,
                    (object?)request.ScopeType?.Trim().ToUpperInvariant() ?? DBNull.Value,
                    (object?)departmentId ?? DBNull.Value,
                    (object?)request.Priority ?? DBNull.Value,
                    (object?)request.IsEnabled ?? DBNull.Value,
                    request.Description is not null,
                    updateDepartment);
                return rows.SingleOrDefault() ?? throw RoutingPolicyNotFound();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new ApiException(422, "INVALID_ROUTING_POLICY_REFERENCE",
                    "A referenced routing policy record is invalid.");
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
            {
                throw InvalidRequest(new Dictionary<string, string[]>
                {
                    ["request"] = ["The routing policy fields do not satisfy the scope and priority rules."]
                });
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                ex.ConstraintName == "uq_routing_policy_active_definition")
            {
                throw RoutingPolicyAlreadyExists();
            }
        }

        var policy = await db.Set<RoutingPolicy>()
            .SingleOrDefaultAsync(p => p.PolicyId == policyId && p.IsActive, ct)
            ?? throw RoutingPolicyNotFound();
        var oldValues = JsonSerializer.Serialize(new
        {
            policy.PolicyName,
            policy.Description,
            policy.ModelId,
            policy.TaskCategory,
            policy.ScopeType,
            policy.DepartmentId,
            policy.Priority,
            policy.IsEnabled
        });

        policy.PolicyName = policyName;
        if (request.Description is not null)
            policy.Description = description;
        policy.ModelId = modelId;
        policy.TaskCategory = taskCategory;
        policy.ScopeType = scopeType;
        if (updateDepartment)
            policy.DepartmentId = departmentId;
        policy.Priority = priority;
        policy.IsEnabled = isEnabled;
        policy.UpdatedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRoleCode = "SUPER_ADMIN",
            Action = "ROUTING_POLICY_UPDATED",
            TargetType = "ROUTING_POLICY",
            TargetId = policy.PolicyId,
            DepartmentId = policy.DepartmentId,
            OldValues = oldValues,
            NewValues = JsonSerializer.Serialize(new
            {
                policy.PolicyName,
                policy.Description,
                policy.ModelId,
                policy.TaskCategory,
                policy.ScopeType,
                policy.DepartmentId,
                policy.Priority,
                policy.IsEnabled
            }),
            Outcome = "SUCCESS",
            CorrelationId = correlationId,
            OccurredAt = policy.UpdatedAt
        });

        await db.SaveChangesAsync(ct);
        return mapper.Map<RoutingPolicyDto>(policy);
    }

    public async Task DeleteAsync(Guid policyId, Guid actorId, CancellationToken ct)
    {
        if (policyId == Guid.Empty)
            throw new ApiException(400, "INVALID_ROUTING_POLICY_ID", "A valid routing policy ID is required.");

        if (db.Database.IsNpgsql())
        {
            try
            {
                var statuses = await db.ReadProcedureAsync(
                    "CALL ai_orchestration.api_delete_routing_policy($1, $2, $3, 'api_rows')",
                    row => row.GetString(0), ct, policyId, actorId, Guid.NewGuid());

                switch (statuses.Single())
                {
                    case "DELETED":
                        return;
                    case "ALREADY_DELETED":
                        throw RoutingPolicyAlreadyDeleted();
                    case "NOT_FOUND":
                        throw RoutingPolicyNotFound();
                    default:
                        throw new InvalidOperationException("The routing policy delete procedure returned an unknown status.");
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw RoutingPolicyDeleteConflict();
            }
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var policy = await db.RoutingPolicies
            .SingleOrDefaultAsync(p => p.PolicyId == policyId, ct);
        if (policy is null)
            throw RoutingPolicyNotFound();
        if (!policy.IsActive)
            throw RoutingPolicyAlreadyDeleted();

        policy.IsActive = false;

        var now = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRoleCode = "SUPER_ADMIN",
            Action = "ROUTING_POLICY_DELETED",
            TargetType = "ROUTING_POLICY",
            TargetId = policy.PolicyId,
            DepartmentId = policy.DepartmentId,
            OldValues = JsonSerializer.Serialize(new
            {
                policy.PolicyName,
                policy.TaskCategory,
                policy.ScopeType,
                policy.ModelId,
                policy.Priority,
                policy.IsEnabled
            }),
            Outcome = "SUCCESS",
            CorrelationId = Guid.NewGuid(),
            OccurredAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) when (!db.Database.IsRelational())
        {
            db.ChangeTracker.Clear();
            var currentPolicy = await db.RoutingPolicies.AsNoTracking()
                .SingleOrDefaultAsync(p => p.PolicyId == policyId, ct);
            if (currentPolicy is null)
                throw RoutingPolicyNotFound();
            if (!currentPolicy.IsActive)
                throw RoutingPolicyAlreadyDeleted();
            throw;
        }

        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    private static RoutingPolicyDto ReadPolicy(NpgsqlDataReader row) =>
        new(
            row.GetGuid(row.GetOrdinal("policy_id")),
            row.GetString(row.GetOrdinal("policy_name")),
            row.IsDBNull(row.GetOrdinal("description"))
                ? null
                : row.GetString(row.GetOrdinal("description")),
            row.GetGuid(row.GetOrdinal("model_id")),
            row.GetString(row.GetOrdinal("task_category")),
            row.GetString(row.GetOrdinal("scope_type")),
            row.IsDBNull(row.GetOrdinal("department_id"))
                ? null
                : row.GetGuid(row.GetOrdinal("department_id")),
            row.GetInt32(row.GetOrdinal("priority")),
            row.GetBoolean(row.GetOrdinal("is_enabled")),
            row.GetBoolean(row.GetOrdinal("is_active")),
            row.GetGuid(row.GetOrdinal("created_by")),
            row.GetFieldValue<DateTime>(row.GetOrdinal("created_at")),
            row.GetFieldValue<DateTime>(row.GetOrdinal("updated_at")));

    private Task<bool> HasActiveDefinitionAsync(
        Guid modelId,
        string taskCategory,
        string scopeType,
        Guid? departmentId,
        Guid? excludedPolicyId,
        CancellationToken ct) =>
        db.Set<RoutingPolicy>().AsNoTracking().AnyAsync(policy =>
            policy.IsActive &&
            policy.ModelId == modelId &&
            policy.TaskCategory == taskCategory &&
            policy.ScopeType == scopeType &&
            policy.DepartmentId == departmentId &&
            (excludedPolicyId == null || policy.PolicyId != excludedPolicyId), ct);

    private static ApiException InvalidRequest(IReadOnlyDictionary<string, string[]> errors) =>
        new(400, "INVALID_ROUTING_POLICY", "One or more routing policy fields are invalid.", errors);

    private static ApiException RoutingPolicyNotFound() =>
        new(404, "ROUTING_POLICY_NOT_FOUND", "The routing policy was not found.");

    private static ApiException RoutingPolicyAlreadyDeleted() =>
        new(409, "ROUTING_POLICY_ALREADY_DELETED", "The routing policy has already been deleted.");

    private static ApiException RoutingPolicyAlreadyExists() =>
        new(409, "ROUTING_POLICY_ALREADY_EXISTS",
            "An active routing policy already exists for this model, task category, and scope.");

    private static ApiException RoutingPolicyDeleteConflict() =>
        new(409, "ROUTING_POLICY_DELETE_CONFLICT",
            "The routing policy cannot be deleted while other records reference it.");
}
