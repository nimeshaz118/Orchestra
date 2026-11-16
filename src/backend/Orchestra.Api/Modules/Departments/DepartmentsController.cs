using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.Departments;

[ApiController]
public sealed class DepartmentsController(DepartmentsService departments) : ControllerBase
{
    [AllowAnonymous, HttpGet("api/v1/public/departments")]
    public async Task<IActionResult> Public(CancellationToken ct)
        => Ok(await departments.PublicListAsync(ct));

    [Authorize(Roles = "ADMIN,SUPER_ADMIN"), HttpGet("api/v1/admin/departments")]
    public async Task<IActionResult> AdminList(CancellationToken ct)
        => Ok(await departments.AdminListAsync(ct));

    [Authorize(Roles = "SUPER_ADMIN"), HttpGet("api/v1/super-admin/departments")]
    public async Task<IActionResult> SuperAdminList(CancellationToken ct)
        => Ok(await departments.AdminListAsync(ct));

    [Authorize(Roles = "SUPER_ADMIN"), HttpGet("api/v1/super-admin/departments/{departmentId:guid}")]
    public async Task<IActionResult> SuperAdminGet(Guid departmentId, CancellationToken ct)
    => Ok(await departments.GetByIdAsync(departmentId, ct));

    [Authorize(Roles = "SUPER_ADMIN"), HttpPatch("api/v1/super-admin/departments/{departmentId:guid}")]
    public async Task<IActionResult> SuperAdminUpdate(
        Guid departmentId, UpdateDepartmentRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await departments.UpdateAsync(departmentId, request, actor, ct));
    }

    [Authorize(Roles = "SUPER_ADMIN"), HttpPost("api/v1/super-admin/departments/{departmentId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid departmentId, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await departments.ActivateAsync(departmentId, actor, ct));
    }

    [Authorize(Roles = "SUPER_ADMIN"), HttpPost("api/v1/super-admin/departments/{departmentId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid departmentId, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await departments.DeactivateAsync(departmentId, actor, ct));
    }

    [Authorize(Roles = "ADMIN,SUPER_ADMIN"), HttpPost("api/v1/admin/departments")]
    public async Task<IActionResult> Create(CreateDepartmentRequest request, CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var result = await departments.CreateAsync(request, id, ct);
        return StatusCode(201, result);
    }

    [Authorize(Roles = "SUPER_ADMIN"), HttpPost("api/v1/super-admin/departments")]
    public Task<IActionResult> SuperAdminCreate(CreateDepartmentRequest request, CancellationToken ct)
        => Create(request, ct);
}
