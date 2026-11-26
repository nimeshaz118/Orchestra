using Microsoft.AspNetCore.Authorization;
using Orchestra.Api.Common;

namespace Orchestra.Api.Modules.Maintenance;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class MaintenanceAiWorkAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class MaintenanceRecoveryAttribute : Attribute;

public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null || !context.Request.Path.StartsWithSegments("/api/v1") ||
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            endpoint.Metadata.GetMetadata<IAuthorizeData>() is null ||
            (context.User.IsInRole("SUPER_ADMIN") &&
                endpoint.Metadata.GetMetadata<MaintenanceRecoveryAttribute>() is not null))
        {
            await next(context);
            return;
        }

        var clientAbort = context.RequestAborted;
        var isAiWork = endpoint.Metadata.GetMetadata<MaintenanceAiWorkAttribute>() is not null;
        var maintenance = context.RequestServices.GetRequiredService<MaintenanceService>();
        await using var active = await maintenance.AdmitAsync(isAiWork, clientAbort);
        using var requestAbort = CancellationTokenSource.CreateLinkedTokenSource(clientAbort, active.Token);
        context.RequestAborted = requestAbort.Token;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (active.Token.IsCancellationRequested &&
            !clientAbort.IsCancellationRequested && !context.Response.HasStarted)
        {
            throw new ApiException(503, "MAINTENANCE_CANCELLED", active.PublicMessage!, retryable: true);
        }
        finally
        {
            context.RequestAborted = clientAbort;
        }
    }
}
