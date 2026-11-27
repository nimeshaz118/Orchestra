namespace Orchestra.Api.Modules.RoutingPolicies.DTOs;

public sealed record CreateRoutingPolicyRequest(
    string? PolicyName,
    string? Description,
    Guid? ModelId,
    string? TaskCategory,
    string? ScopeType,
    Guid? DepartmentId,
    int Priority,
    bool IsEnabled = true);
