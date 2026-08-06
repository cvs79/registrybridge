using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RegistryBridge.Api;
using RegistryBridge.Api.ArtifactExecution;
using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.Data;

RegistryBridgeCommand.EnsureServe(args);

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RegistryBridge")
    ?? throw new InvalidOperationException("The RegistryBridge database connection string is required.");

builder.Services.AddDbContext<RegistryBridgeDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.AddSingleton<IArtifactExecutionGateway, UnavailableArtifactExecutionGateway>();
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "application",
        () => HealthCheckResult.Healthy(),
        tags: ["live", "ready"])
    .AddDbContextCheck<RegistryBridgeDbContext>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks(
    "/health",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live")
    });
app.MapHealthChecks(
    "/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready")
    });

app.MapGet(
    "/api/catalog",
    async (RegistryBridgeDbContext database, CancellationToken cancellationToken) =>
    {
        var currentRevision = await database.CatalogRevisions
            .OrderByDescending(revision => revision.CreatedAt)
            .Select(revision => new CatalogRevisionResponse(revision.Id, revision.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(
            new CatalogResponse(
                currentRevision,
                Array.Empty<CatalogEntryResponse>()));
    });

using (var scope = app.Services.CreateScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
    await migrator.MigrateAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

public partial class Program;
