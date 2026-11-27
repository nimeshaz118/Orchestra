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

        if (db.Database.IsNpgsql())
            return await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_list_routing_policies_by_department($1, 'api_rows')",
                ReadPolicy, ct, departmentId);

        if (!await db.Departments.AsNoTracking().AnyAsync(d => d.DepartmentId == departmentId, ct))
            throw new ApiException(404, "DEPARTMENT_NOT_FOUND", "The department was not found.");

        return await db.Set<RoutingPolicy>()
            .AsNoTracking()
            .Where(p => p.DepartmentId == departmentId)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.PolicyName)
            .ProjectTo<RoutingPolicyDto>(mapper.ConfigurationProvider)
            .ToListAsync(ct);
    }

    public async Task<RoutingPolicyDto> GetByIdAsync(Guid policyId, CancellationToken ct)
    {
        if (policyId == Guid.Empty)
            throw new ApiException(400, "INVALID_ROUTING_POLICY_ID", "A valid routing policy ID is required.");

        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_routing_policy($1, 'api_rows')",
                ReadPolicy, ct, policyId);
            return rows.SingleOrDefault()
                ?? throw RoutingPolicyNotFound();
        }

        var policy = await db.Set<RoutingPolicy>()
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.PolicyId == policyId, ct);
        return mapper.Map<RoutingPolicyDto>(policy ?? throw RoutingPolicyNotFound());
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
            UpdatedAt = now
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

    private static RoutingPolicyDto ReadPolicy(NpgsqlDataReader row) =>
        new(row.GetGuid(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2),
            row.GetGuid(3), row.GetString(4), row.GetString(5),
            row.IsDBNull(6) ? null : row.GetGuid(6), row.GetInt32(7), row.GetBoolean(8),
            row.GetGuid(9), row.GetFieldValue<DateTime>(10), row.GetFieldValue<DateTime>(11));

    private static ApiException InvalidRequest(IReadOnlyDictionary<string, string[]> errors) =>
        new(400, "INVALID_ROUTING_POLICY", "One or more routing policy fields are invalid.", errors);

    private static ApiException RoutingPolicyNotFound() =>
        new(404, "ROUTING_POLICY_NOT_FOUND", "The routing policy was not found.");
}
