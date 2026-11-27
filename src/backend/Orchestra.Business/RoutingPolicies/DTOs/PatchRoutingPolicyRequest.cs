namespace Orchestra.Api.Modules.RoutingPolicies.DTOs;

/// <summary>Optional field values for updating an active routing policy.</summary>
public sealed record PatchRoutingPolicyRequest(
    string? PolicyName = null,
    string? Description = null,
    Guid? ModelId = null,
    string? TaskCategory = null,
    string? ScopeType = null,
    Guid? DepartmentId = null,
    int? Priority = null,
    bool? IsEnabled = null);
