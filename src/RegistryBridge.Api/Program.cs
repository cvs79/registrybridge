using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using RegistryBridge.Api;
using RegistryBridge.Api.ArtifactExecution;
using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.Data;
using RegistryBridge.Api.Deployment;
using RegistryBridge.Api.SynchronizationRuns;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<RegistryBridgeDbContext>("registrybridge");
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.AddSingleton<IArtifactExecutionGateway, UnavailableArtifactExecutionGateway>();
builder.Services.AddSingleton<SynchronizationRunCoordinator>();
builder.Services.Configure<DeploymentConfiguration>(
    builder.Configuration.GetSection("Deployment"));
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<RegistryBridgeDbContext>(
        "postgresql");

var app = builder.Build();

if (args.Length == 1 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
    if ((await database.Database.GetPendingMigrationsAsync()).Any())
    {
        Console.Error.WriteLine("RegistryBridge cannot run while database migrations are pending.");
        Environment.ExitCode = 1;
        return;
    }

    var coordinator = scope.ServiceProvider.GetRequiredService<SynchronizationRunCoordinator>();
    var run = await coordinator.StartScheduledAndWaitAsync(app.Lifetime.ApplicationStopping);
    if (run.Status == SynchronizationRunStatus.Completed)
    {
        var failed = await database.ArtifactOutcomes.AnyAsync(
            outcome => outcome.SynchronizationRunId == run.Id
                && (outcome.Disposition == RegistryBridge.Api.SynchronizationRuns.ArtifactOutcomeDisposition.Failed
                    || outcome.Disposition == RegistryBridge.Api.SynchronizationRuns.ArtifactOutcomeDisposition.Blocked
                    || outcome.Disposition == RegistryBridge.Api.SynchronizationRuns.ArtifactOutcomeDisposition.Conflict));
        Environment.ExitCode = failed ? 1 : 0;
    }

    return;
}

app.MapDefaultEndpoints();
app.MapHealthChecks(
    "/healthz",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live")
    });
app.MapHealthChecks("/readyz");

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
                deployment.Value.RunLogLimitBytes,
                deployment.Value.CredentialHandles
                    .Select(handle => new CredentialHandleResponse(handle.Name, handle.Type))
                    .ToArray())));

app.MapGet(
    "/api/runs",
    async (RegistryBridgeDbContext database, CancellationToken cancellationToken) =>
    {
        var runs = await database.SynchronizationRuns
            .AsNoTracking()
            .OrderByDescending(run => run.CreatedAt)
            .ThenByDescending(run => run.Id)
            .ToArrayAsync(cancellationToken);
        return TypedResults.Ok(runs.Select(SynchronizationRunResponses.ToResponse).ToArray());
    });

app.MapGet(
    "/api/runs/{runId:guid}",
    async Task<IResult> (
        Guid runId,
        RegistryBridgeDbContext database,
        CancellationToken cancellationToken) =>
    {
        var run = await database.SynchronizationRuns
            .AsNoTracking()
            .Include(candidate => candidate.Outcomes)
            .Include(candidate => candidate.Logs)
            .SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        return run is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(SynchronizationRunResponses.ToDetailResponse(run));
    });

app.MapPost(
    "/api/runs",
    async Task<IResult> (
        SynchronizationRunCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        var result = await coordinator.StartManualAsync(cancellationToken);
        return result.Run is not null
            ? TypedResults.Accepted(
                $"/api/runs/{result.Run.Id}",
                SynchronizationRunResponses.ToResponse(result.Run))
            : TypedResults.Conflict(
                new ManualSynchronizationRunConflictResponse(
                    SynchronizationRunResponses.ToResponse(result.ActiveRun!)));
    });

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
                        entry.Enabled,
                        entry.Kind,
                        entry.SourceVersion,
                        entry.ExpectedDigest))
                .ToArray(),
            (request.VulnerabilityExceptions ?? [])
                .Select(
                    exception => new VulnerabilityExceptionDefinition(
                        exception.Id,
                        exception.ImageDigest,
                        exception.VulnerabilityIds,
                        exception.Reason))
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
