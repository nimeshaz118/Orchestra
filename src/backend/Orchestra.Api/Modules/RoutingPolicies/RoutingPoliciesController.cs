using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Api.Modules.RoutingPolicies.DTOs;

namespace Orchestra.Api.Modules.RoutingPolicies;

[ApiController]
[Route("api/v1/super-admin/routing-policies")]
[Authorize(Roles = "SUPER_ADMIN")]
public sealed class RoutingPoliciesController(RoutingPoliciesService routingPolicies) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await routingPolicies.ListAsync(ct));

    [HttpGet("department/{departmentId:guid}")]
    public async Task<IActionResult> ListByDepartment(Guid departmentId, CancellationToken ct)
        => Ok(await routingPolicies.ListByDepartmentAsync(departmentId, ct));

    [HttpGet("{policyId:guid}")]
    public async Task<IActionResult> Get(Guid policyId, CancellationToken ct)
        => Ok(await routingPolicies.GetByIdAsync(policyId, ct));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateRoutingPolicyRequest request,
        CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return StatusCode(201, await routingPolicies.CreateAsync(request, actorId, ct));
    }
}
