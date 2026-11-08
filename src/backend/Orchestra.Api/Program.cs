using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<OrchestraDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("OrchestraDB")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrchestraDbContext>(
        name: "postgresql",
        tags: new[] { "ready" });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
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