using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.Domains;

[ApiController]
[Route("api/v1/admin/domains")]
[Authorize(Roles = "ADMIN,SUPER_ADMIN")]
public sealed class DomainsController(DomainsService domains) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await domains.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateDomainRequest request,
        CancellationToken ct)
    {
        var actorId = Guid.Parse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var result = await domains.CreateAsync(request, actorId, ct);

        return result.Created
            ? StatusCode(201, result.Domain)
            : Ok(result.Domain);
    }

   /* [HttpDelete("{domainId}")]
    public async Task<IActionResult> Delete(
        Guid domainId,
        CancellationToken ct)
    {
        var actorId = Guid.Parse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        await domains.DeleteAsync(domainId, actorId, ct);
        return NoContent();
    }
    */
}