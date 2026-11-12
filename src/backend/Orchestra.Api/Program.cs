using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Controllers.UserPreference;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Auth;
using Orchestra.Api.Modules.Departments;
using Orchestra.Api.Modules.Users;
using Orchestra.Api.Modules.Users.Service;
using Orchestra.Api.Modules.Users.UseCases;
using Orchestra.Api.Modules.Users.Interfaces;
using Orchestra.Api.Modules.Domains;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(provider =>
{
    var settings = provider.GetRequiredService<IConfiguration>()
        .GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
    settings.Validate();
    return settings;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<OrchestraDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrchestraDb")));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MeService>();
builder.Services.AddScoped<AvatarService>();
builder.Services.AddScoped<DepartmentsService>();
builder.Services.AddScoped<UserPreferenceService>();
builder.Services.AddScoped<DomainsService>();
builder.Services.AddSingleton<CodeService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<RevokeSessionUseCase>();
builder.Services.AddScoped<RevokeOtherSessionsUseCase>();

builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<PrivilegedAccountBootstrap>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        var settings = builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
        settings.Validate();
        options.TokenValidationParameters = new TokenService(settings).ValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var disabled = context.HttpContext.Items.ContainsKey("AccountUnavailable");
                context.Response.StatusCode = disabled ? 403 : 401;
                if (!disabled) context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsJsonAsync(new ApiError(
                    disabled ? "ACCOUNT_DISABLED" : "INVALID_SESSION",
                    disabled ? "This account is unavailable." : "Please sign in again.",
                    context.HttpContext.TraceIdentifier));
            },
            OnTokenValidated = async context =>
            {
                var rawId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var rawStamp = context.Principal?.FindFirstValue("stamp");
                if (!Guid.TryParse(rawId, out var id) || !Guid.TryParse(rawStamp, out var stamp) ||
                    !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId))
                {
                    context.Fail("Invalid identity.");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<OrchestraDbContext>();
                var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id);
                if (user is null || user.DeletedAt != null || user.SecurityStamp != stamp ||
                    !context.Principal!.IsInRole(user.RoleCode))
                {
                    context.Fail("Invalid identity.");
                    return;
                }
                if (!await db.RefreshTokens.AsNoTracking().AnyAsync(t => t.UserId == id &&
                    t.FamilyId == sessionId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow,
                    context.HttpContext.RequestAborted))
                {
                    context.Fail("Session is no longer active.");
                    return;
                }
                if (user.Status != "ACTIVE" || user.EmailVerifiedAt == null)
                {
                    context.HttpContext.Items["AccountUnavailable"] = true;
                    context.Fail("Account is unavailable.");
                    return;
                }
                if (user.MustChangePassword)
                    context.Principal!.AddIdentity(new ClaimsIdentity([new Claim("must_change_password", "true")]));
            }
        };
    });
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("Frontend:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));


builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiError("RATE_LIMITED",
            "Too many requests. Try again later.", context.HttpContext.TraceIdentifier), ct);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(field => field.Value?.Errors.Count > 0)
            .ToDictionary(
                field => field.Key,
                field => field.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The supplied value is invalid."
                        : error.ErrorMessage)
                    .ToArray());

        return new ObjectResult(new ApiError(
            "VALIDATION_FAILED", "The request contains invalid fields.",
            context.HttpContext.TraceIdentifier, Errors: errors))
        { StatusCode = context.HttpContext.Request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ? 400 : 422 };
    };
});


builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrchestraDbContext>(
        name: "postgresql",
        tags: new[] { "ready" });

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is ApiException apiError)
    {
        context.Response.StatusCode = apiError.StatusCode;
        await context.Response.WriteAsJsonAsync(new ApiError(
            apiError.Code,
            apiError.Message,
            context.TraceIdentifier,
            apiError.Retryable,
            apiError.Errors));
        return;
    }

    app.Logger.LogError(exception, "Unhandled API error. Request ID: {RequestId}", context.TraceIdentifier);
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new ApiError("INTERNAL_ERROR", "An unexpected error occurred.",
        context.TraceIdentifier));
}));

app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;
    var status = context.Response.StatusCode;

    var (code, message, retryable) = status switch
    {
        400 => ("BAD_REQUEST", "The request could not be understood.", false),
        401 => ("UNAUTHENTICATED", "Sign in to access this resource.", false),
        403 => ("FORBIDDEN", "You do not have permission to perform this action.", false),
        404 => ("NOT_FOUND", "The requested resource was not found.", false),
        405 => ("METHOD_NOT_ALLOWED", "This HTTP method is not supported for this resource.", false),
        409 => ("CONFLICT", "The request conflicts with the current resource state.", false),
        413 => ("REQUEST_TOO_LARGE", "The request body is too large.", false),
        415 => ("UNSUPPORTED_MEDIA_TYPE", "The request content type is not supported.", false),
        422 => ("VALIDATION_FAILED", "The request contains invalid fields.", false),
        429 => ("RATE_LIMITED", "Too many requests. Try again later.", true),
        _ => ("REQUEST_FAILED", "The request could not be completed.", false)
    };

    await context.Response.WriteAsJsonAsync(
        new ApiError(code, message, context.TraceIdentifier, retryable),
        context.RequestAborted);
});

if (app.Environment.IsDevelopment()) app.MapOpenApi();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.HasClaim("must_change_password", "true") &&
        !context.Request.Path.StartsWithSegments("/api/v1/auth/change-password") &&
        !context.Request.Path.StartsWithSegments("/api/v1/auth/session") &&
        !context.Request.Path.StartsWithSegments("/api/v1/auth/logout-all") &&
        !context.Request.Path.StartsWithSegments("/api/v1/auth/logout"))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new ApiError("PASSWORD_CHANGE_REQUIRED",
            "Change your initial password before continuing.", context.TraceIdentifier));
        return;
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();

// Confirms that the API can respond, without checking dependencies.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// Checks PostgreSQL connectivity through OrchestraDbContext.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;
