namespace Orchestra.Api.Modules.RoutingPolicies.DTOs;

public sealed record RoutingPolicyDto(
    Guid PolicyId,
    string PolicyName,
    string? Description,
    Guid ModelId,
    string TaskCategory,
    string ScopeType,
    Guid? DepartmentId,
    int Priority,
    bool IsEnabled,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);
