namespace Orchestra.Api.Modules.RoutingPolicies.DTOs;

/// <summary>Fields required to create a routing policy.</summary>
public sealed record CreateRoutingPolicyRequest(
    string? PolicyName,
    string? Description,
    Guid? ModelId,
    string? TaskCategory,
    string? ScopeType,
    Guid? DepartmentId,
    int Priority,
    bool IsEnabled = true);
