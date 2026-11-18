using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Api.Modules.Catalog;
using Orchestra.Api.Modules.Users;

namespace Orchestra.Api.Modules.Models;

[ApiController]
public sealed class ModelsController(ModelsService models, CatalogTestsService tests) : ControllerBase
{
    [Authorize(Roles = "SUPER_ADMIN"), HttpGet("api/v1/super-admin/models")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await models.ListAsync(ct));

    [Authorize(Roles = "SUPER_ADMIN"), HttpPost("api/v1/super-admin/models")]
    public async Task<IActionResult> Create(CreateModelRequest request, CancellationToken ct)
    {
        var model = await models.CreateAsync(request, User.GetUserId(), ct);
        return Created($"/api/v1/super-admin/models/{model.Id}", model);
    }

    [Authorize(Roles = "SUPER_ADMIN"), HttpGet("api/v1/super-admin/models/{modelId:guid}")]
    public async Task<IActionResult> Get(Guid modelId, CancellationToken ct)
        => Ok(await models.GetByIdAsync(modelId, ct));

    [Authorize(Roles = "SUPER_ADMIN"), HttpPost("api/v1/super-admin/models/{modelId:guid}/test")]
    public async Task<IActionResult> Test(Guid modelId, CancellationToken ct)
        => Ok(await tests.TestModelAsync(modelId, ct));
}
