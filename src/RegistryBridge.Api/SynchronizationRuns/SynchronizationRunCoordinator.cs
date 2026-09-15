using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RegistryBridge.Api.ArtifactExecution;
using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.Data;
using RegistryBridge.Api.Deployment;

namespace RegistryBridge.Api.SynchronizationRuns;

public sealed class SynchronizationRunCoordinator(
    IServiceScopeFactory scopeFactory,
    IOptions<DeploymentConfiguration> deployment)
{
    private const long RunLockId = 772320949489648950;
    private readonly DeploymentConfiguration deployment = deployment.Value;

    public async Task<ManualSynchronizationRunStartResult> StartManualAsync(CancellationToken cancellationToken)
    {
        var start = await TryStartAsync(SynchronizationRunOrigin.Manual, cancellationToken);
        return start.Started
            ? new ManualSynchronizationRunStartResult(start.Run, null)
            : new ManualSynchronizationRunStartResult(null, start.Run);
    }

    public async Task<SynchronizationRun> StartScheduledAndWaitAsync(CancellationToken cancellationToken)
    {
        var start = await TryStartAsync(SynchronizationRunOrigin.Scheduled, cancellationToken);
        if (!start.Started)
        {
            return start.Run!;
        }

        await start.Execution!.WaitAsync(cancellationToken);
        return await LoadRunAsync(start.Run!.Id, cancellationToken);
    }

    private async Task<SynchronizationRunStartResult> TryStartAsync(
        SynchronizationRunOrigin origin,
        CancellationToken cancellationToken)
    {
        var connectionString = await GetConnectionStringAsync(cancellationToken);
        var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);

        if (!await TryAcquireLockAsync(lockConnection, cancellationToken))
        {
            await lockConnection.DisposeAsync();
            var activeRun = await FindActiveRunAsync(cancellationToken);

            if (origin == SynchronizationRunOrigin.Scheduled)
            {
                var skippedRun = await CreateSkippedRunAsync(cancellationToken);
                return new SynchronizationRunStartResult(false, skippedRun, null);
            }

            return new SynchronizationRunStartResult(false, activeRun, null);
        }

        try
        {
            var run = await CreateRunningRunAsync(origin, cancellationToken);
            var execution = ExecuteAndReleaseAsync(run.Id, lockConnection);
            lockConnection = null!;

            if (origin == SynchronizationRunOrigin.Manual)
            {
                _ = execution;
            }

            return new SynchronizationRunStartResult(true, run, execution);
        }
        catch
        {
            await ReleaseAndDisposeAsync(lockConnection);
            throw;
        }
    }

    private async Task<SynchronizationRun> CreateRunningRunAsync(
        SynchronizationRunOrigin origin,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        var activeRuns = await database.SynchronizationRuns
            .Where(run => run.Status == SynchronizationRunStatus.Running)
            .ToArrayAsync(cancellationToken);
        foreach (var activeRun in activeRuns)
        {
            activeRun.Status = SynchronizationRunStatus.Abandoned;
            activeRun.CompletedAt = DateTimeOffset.UtcNow;
        }

        var catalog = await CatalogStates.LoadAsync(database, cancellationToken);
        var run = new SynchronizationRun
        {
            Id = Guid.NewGuid(),
            CatalogRevisionId = catalog.CurrentRevisionId,
            Origin = origin,
            Status = SynchronizationRunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow
        };
        database.SynchronizationRuns.Add(run);
        await database.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task<SynchronizationRun> CreateSkippedRunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        var catalog = await CatalogStates.LoadAsync(database, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var run = new SynchronizationRun
        {
            Id = Guid.NewGuid(),
            CatalogRevisionId = catalog.CurrentRevisionId,
            Origin = SynchronizationRunOrigin.Scheduled,
            Status = SynchronizationRunStatus.Skipped,
            CreatedAt = now,
            CompletedAt = now
        };
        database.SynchronizationRuns.Add(run);
        await database.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task ExecuteAndReleaseAsync(Guid runId, NpgsqlConnection lockConnection)
    {
        try
        {
            await ExecuteAsync(runId);
        }
        catch (Exception exception)
        {
            await MarkAbandonedAsync(runId, exception);
        }
        finally
        {
            await ReleaseAndDisposeAsync(lockConnection);
        }
    }

    private async Task ExecuteAsync(Guid runId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        var gateway = scope.ServiceProvider.GetRequiredService<IArtifactExecutionGateway>();
        var run = await database.SynchronizationRuns
            .Include(candidate => candidate.CatalogRevision)
            .SingleAsync(candidate => candidate.Id == runId);
        var writer = new RunLogWriter(database, run, deployment);
        await writer.WriteAsync("run.started", new { runId, run.Origin });

        if (run.CatalogRevision is not null)
        {
            var definition = CatalogDefinitions.Deserialize(run.CatalogRevision.Definition);
            foreach (var (entry, order) in definition.Entries.Select((entry, order) => (entry, order)))
            {
                if (!entry.Enabled)
                {
                    await AddOutcomeAsync(
                        database,
                        run,
                        entry,
                        order,
                        ArtifactOutcomeDisposition.NotSelected,
                        null,
                        writer);
                    continue;
                }

                try
                {
                    await writer.WriteAsync("entry.started", new { entry.Id, entry.TargetRepository, entry.TargetTag });
                    var result = await gateway.ExecuteAsync(
                        new ArtifactExecutionRequest(
                            entry.Id,
                            entry.TargetRepository,
                            entry.TargetTag,
                            entry.SourceReference),
                        CancellationToken.None);
                    await AddOutcomeAsync(
                        database,
                        run,
                        entry,
                        order,
                        MapDisposition(result.Disposition),
                        result.Detail,
                        writer);
                }
                catch (Exception exception)
                {
                    await AddOutcomeAsync(
                        database,
                        run,
                        entry,
                        order,
                        ArtifactOutcomeDisposition.Failed,
                        exception.Message,
                        writer);
                }
            }
        }

        run.Status = SynchronizationRunStatus.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await writer.WriteAsync("run.completed", new { runId, run.Status });
        await database.SaveChangesAsync();
        await CleanupExpiredRecordsAsync(database);
    }

    private static async Task AddOutcomeAsync(
        RegistryBridgeDbContext database,
        SynchronizationRun run,
        CatalogEntryDefinition entry,
        int order,
        ArtifactOutcomeDisposition disposition,
        string? detail,
        RunLogWriter writer)
    {
        var redactedDetail = writer.Redact(detail);
        database.ArtifactOutcomes.Add(
            new ArtifactOutcomeRecord
            {
                Id = Guid.NewGuid(),
                SynchronizationRunId = run.Id,
                CatalogEntryId = entry.Id,
                Order = order,
                Disposition = disposition,
                Detail = redactedDetail
            });
        await writer.WriteAsync(
            "entry.completed",
            new { entry.Id, entry.TargetRepository, disposition, detail = redactedDetail });
        await database.SaveChangesAsync();
    }

    private async Task MarkAbandonedAsync(Guid runId, Exception exception)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        var run = await database.SynchronizationRuns.SingleOrDefaultAsync(candidate => candidate.Id == runId);
        if (run is null)
        {
            return;
        }

        run.Status = SynchronizationRunStatus.Abandoned;
        run.CompletedAt = DateTimeOffset.UtcNow;
        var writer = new RunLogWriter(database, run, deployment);
        await writer.WriteAsync("run.abandoned", new { error = exception.Message });
        await database.SaveChangesAsync();
    }

    private async Task CleanupExpiredRecordsAsync(RegistryBridgeDbContext database)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, deployment.RetentionDays));
        await database.SynchronizationRuns
            .Where(run => run.CompletedAt != null
                && run.CompletedAt < cutoff
                && run.Status != SynchronizationRunStatus.Running)
            .ExecuteDeleteAsync();
    }

    private async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        await database.Database.OpenConnectionAsync(cancellationToken);
        return database.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The RegistryBridge database connection string is required.");
    }

    private async Task<SynchronizationRun?> FindActiveRunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        return await database.SynchronizationRuns
            .AsNoTracking()
            .Where(run => run.Status == SynchronizationRunStatus.Running)
            .OrderBy(run => run.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SynchronizationRun> LoadRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        return await database.SynchronizationRuns.AsNoTracking()
            .SingleAsync(run => run.Id == runId, cancellationToken);
    }

    private static async Task<bool> TryAcquireLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(@lock_id);",
            connection);
        command.Parameters.AddWithValue("lock_id", RunLockId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The run lock did not return a result."));
    }

    private static async Task ReleaseAndDisposeAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(@lock_id);",
            connection);
        command.Parameters.AddWithValue("lock_id", RunLockId);
        await command.ExecuteNonQueryAsync();
        await connection.DisposeAsync();
    }

    private static ArtifactOutcomeDisposition MapDisposition(
        ArtifactExecution.ArtifactOutcomeDisposition disposition) =>
        disposition switch
        {
            ArtifactExecution.ArtifactOutcomeDisposition.Promoted => ArtifactOutcomeDisposition.Promoted,
            ArtifactExecution.ArtifactOutcomeDisposition.Verified => ArtifactOutcomeDisposition.Verified,
            ArtifactExecution.ArtifactOutcomeDisposition.Blocked => ArtifactOutcomeDisposition.Blocked,
            ArtifactExecution.ArtifactOutcomeDisposition.Conflict => ArtifactOutcomeDisposition.Conflict,
            _ => ArtifactOutcomeDisposition.Failed
        };

    private sealed record SynchronizationRunStartResult(
        bool Started,
        SynchronizationRun? Run,
        Task? Execution);
}

public sealed record ManualSynchronizationRunStartResult(
    SynchronizationRun? Run,
    SynchronizationRun? ActiveRun);

internal sealed class RunLogWriter(
    RegistryBridgeDbContext database,
    SynchronizationRun run,
    DeploymentConfiguration deployment)
{
    private static readonly Regex CredentialBearingUrl = new(
        @"(?<scheme>https?://)[^/\s@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private int storedBytes;

    public string? Redact(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var redacted = CredentialBearingUrl.Replace(value, "${scheme}[REDACTED]@");
        foreach (var secret in deployment.SecretValues.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            redacted = redacted.Replace(
                JsonSerializer.Serialize(secret).Trim('"'),
                "[REDACTED]",
                StringComparison.Ordinal);
        }

        return redacted;
    }

    public Task WriteAsync(string eventType, object data)
    {
        var dataJson = Redact(JsonSerializer.Serialize(data))!;
        Console.WriteLine(JsonSerializer.Serialize(new { eventType, data = JsonSerializer.Deserialize<JsonElement>(dataJson) }));
        if (storedBytes + System.Text.Encoding.UTF8.GetByteCount(dataJson) > deployment.RunLogLimitBytes)
        {
            run.LogsTruncated = true;
            return Task.CompletedTask;
        }

        storedBytes += System.Text.Encoding.UTF8.GetByteCount(dataJson);
        database.RunLogs.Add(
            new RunLog
            {
                Id = Guid.NewGuid(),
                SynchronizationRunId = run.Id,
                OccurredAt = DateTimeOffset.UtcNow,
                EventType = eventType,
                Data = dataJson
            });
        return Task.CompletedTask;
    }
}
