using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.RoutingPolicies.DTOs;

namespace Orchestra.Api.Modules.RoutingPolicies;

[ApiController]
[Route("api/v1/super-admin/routing-policies")]
[Authorize(Roles = "SUPER_ADMIN")]
public sealed class RoutingPoliciesController(RoutingPoliciesService routingPolicies) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(List<RoutingPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await routingPolicies.ListAsync(ct));

    [HttpGet("department/{departmentId:guid}")]
    [ProducesResponseType(typeof(List<RoutingPolicyDto>), StatusCodes.Status200OK)] 
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListByDepartment(Guid departmentId, CancellationToken ct)
        => Ok(await routingPolicies.ListByDepartmentAsync(departmentId, ct));

    [HttpGet("deleted/scope/{scopeType}")]
    [ProducesResponseType(typeof(List<RoutingPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListDeletedByScope(string scopeType, CancellationToken ct)
        => Ok(await routingPolicies.ListDeletedByScopeAsync(scopeType, ct));

    [HttpGet("deleted/department/{departmentId:guid}")]
    [ProducesResponseType(typeof(List<RoutingPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDeletedByDepartment(Guid departmentId, CancellationToken ct)
        => Ok(await routingPolicies.ListDeletedByDepartmentAsync(departmentId, ct));

    [HttpGet("{policyId:guid}")]
    [ProducesResponseType(typeof(RoutingPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid policyId, CancellationToken ct)
        => Ok(await routingPolicies.GetByIdAsync(policyId, ct));

    [HttpPost]
    [ProducesResponseType(typeof(RoutingPolicyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateRoutingPolicyRequest request,
        CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return StatusCode(201, await routingPolicies.CreateAsync(request, actorId, ct));
    }

    [HttpPatch("{policyId:guid}")]
    [ProducesResponseType(typeof(RoutingPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Patch(
        Guid policyId,
        PatchRoutingPolicyRequest request,
        CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await routingPolicies.PatchAsync(policyId, request, actorId, ct));
    }

    [HttpDelete("{policyId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid policyId, CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        await routingPolicies.DeleteAsync(policyId, actorId, ct);
        return Ok(new { message = "Routing policy deleted successfully." });
    }
}
