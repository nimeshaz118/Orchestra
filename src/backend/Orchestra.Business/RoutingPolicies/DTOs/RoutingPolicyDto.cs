namespace Orchestra.Api.Modules.RoutingPolicies.DTOs;

/// <summary>Routing policy details returned by the API.</summary>
public record RoutingPolicyDto(
    Guid PolicyId,
    string PolicyName,
    string? Description,
    Guid ModelId,
    string TaskCategory,
    string ScopeType,
    Guid? DepartmentId,
    int Priority,
    bool IsEnabled,
    bool IsActive,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
