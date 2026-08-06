using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RegistryBridge.Api;
using RegistryBridge.Api.ArtifactExecution;
using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.Data;
using RegistryBridge.Api.Deployment;

RegistryBridgeCommand.EnsureServe(args);

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RegistryBridge")
    ?? throw new InvalidOperationException("The RegistryBridge database connection string is required.");

builder.Services.AddDbContext<RegistryBridgeDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.AddSingleton<IArtifactExecutionGateway, UnavailableArtifactExecutionGateway>();
builder.Services.Configure<DeploymentConfiguration>(
    builder.Configuration.GetSection("Deployment"));
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
        var catalog = await CatalogStates.LoadAsync(database, cancellationToken);

        return TypedResults.Ok(CatalogDefinitions.ToResponse(catalog.CurrentRevision));
    });

app.MapGet(
    "/api/catalog/revisions",
    async (RegistryBridgeDbContext database, CancellationToken cancellationToken) =>
    {
        var revisions = await database.CatalogRevisions
            .OrderByDescending(revision => revision.CreatedAt)
            .ThenByDescending(revision => revision.Id)
            .Select(revision => new CatalogRevisionResponse(revision.Id, revision.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(revisions);
    });

app.MapGet(
    "/api/catalog/revisions/{revisionId:guid}",
    async Task<IResult> (
        Guid revisionId,
        RegistryBridgeDbContext database,
        CancellationToken cancellationToken) =>
    {
        var revision = await database.CatalogRevisions
            .SingleOrDefaultAsync(candidate => candidate.Id == revisionId, cancellationToken);

        return revision is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CatalogDefinitions.ToSnapshotResponse(revision));
    });

app.MapGet(
    "/api/deployment-configuration",
    (Microsoft.Extensions.Options.IOptions<DeploymentConfiguration> deployment) =>
        TypedResults.Ok(
            new DeploymentConfigurationResponse(
                deployment.Value.TargetRegistry,
                deployment.Value.CredentialHandles
                    .Select(handle => new CredentialHandleResponse(handle.Name, handle.Type))
                    .ToArray())));

app.MapPut(
    "/api/catalog",
    async Task<IResult> (
        CatalogSaveRequest request,
        RegistryBridgeDbContext database,
        Microsoft.Extensions.Options.IOptions<DeploymentConfiguration> deployment,
        CancellationToken cancellationToken) =>
    {
        var catalog = await CatalogStates.LoadAsync(database, cancellationToken);
        var currentRevision = catalog.CurrentRevision;

        if (currentRevision?.Id != request.BaseRevisionId)
        {
            return TypedResults.Conflict(
                new CatalogSaveConflictResponse(currentRevision?.Id));
        }

        var validationErrors = CatalogValidation.Validate(
            request,
            deployment.Value.CredentialHandles);
        var priorDefinitions = await database.CatalogRevisions
            .Select(revision => revision.Definition)
            .ToArrayAsync(cancellationToken);

        foreach (var validationError in CatalogValidation.ValidateTargetTagImmutability(
                     request,
                     priorDefinitions.Select(CatalogDefinitions.Deserialize)))
        {
            validationErrors.TryAdd(validationError.Key, validationError.Value);
        }

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var definition = new CatalogDefinition(
            request.Entries
                .Select(
                    entry => new CatalogEntryDefinition(
                        entry.Id,
                        entry.SourceReference,
                        entry.TargetRepository,
                        entry.TargetTag,
                        entry.CredentialHandle,
                        entry.Enabled))
                .ToArray());

        if (currentRevision is not null
            && CatalogDefinitions.IsEquivalent(
                CatalogDefinitions.Deserialize(currentRevision.Definition),
                definition))
        {
            return TypedResults.Conflict(
                new CatalogSaveConflictResponse(
                    currentRevision.Id,
                    "The Catalog has no semantic changes."));
        }

        var revision = new CatalogRevision
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Definition = CatalogDefinitions.Serialize(definition)
        };

        database.CatalogRevisions.Add(revision);
        catalog.CurrentRevision = revision;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(
                new CatalogSaveConflictResponse(
                    null,
                    "The Catalog changed while this save was in progress."));
        }

        return TypedResults.Created(
            $"/api/catalog/revisions/{revision.Id}",
            CatalogDefinitions.ToResponse(revision));
    });

using (var scope = app.Services.CreateScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
    await migrator.MigrateAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

public partial class Program;
