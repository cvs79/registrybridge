using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegistryBridge.Api.ArtifactExecution;
using RegistryBridge.Api.Data;

namespace RegistryBridge.Api.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SynchronizationRunIntegrationTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    public Task InitializeAsync() => ResetDatabaseAsync();

    public Task DisposeAsync() => ResetDatabaseAsync();

    [Fact]
    public async Task StartsAManualSynchronizationRunAndPersistsOrderedOutcomesFromItsCatalogRevision()
    {
        var gateway = new SequencedArtifactExecutionGateway(
            new ArtifactOutcome(ArtifactOutcomeDisposition.Promoted),
            new ArtifactOutcome(ArtifactOutcomeDisposition.Blocked, "policy rejected"));
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString, gateway);
        using var client = application.CreateClient();
        var enabledEntryId = Guid.NewGuid();
        var disabledEntryId = Guid.NewGuid();
        await SaveCatalogAsync(client, enabledEntryId, disabledEntryId, Guid.NewGuid());

        using var startResponse = await client.PostAsync("/api/runs", null);
        using var started = JsonDocument.Parse(await startResponse.Content.ReadAsStreamAsync());
        var runId = started.RootElement.GetProperty("id").GetGuid();
        using var completed = await WaitForRunAsync(client, runId);

        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        Assert.Equal("Completed", completed.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, completed.RootElement.GetProperty("outcomes").GetArrayLength());
        Assert.Equal("Promoted", completed.RootElement.GetProperty("outcomes")[0].GetProperty("disposition").GetString());
        Assert.Equal("Not selected", completed.RootElement.GetProperty("outcomes")[1].GetProperty("disposition").GetString());
        Assert.Equal("Blocked", completed.RootElement.GetProperty("outcomes")[2].GetProperty("disposition").GetString());
        Assert.Equal(enabledEntryId, completed.RootElement.GetProperty("outcomes")[0].GetProperty("catalogEntryId").GetGuid());
        Assert.Equal(disabledEntryId, completed.RootElement.GetProperty("outcomes")[1].GetProperty("catalogEntryId").GetGuid());
        Assert.Equal(2, gateway.Requests.Count);
    }

    [Fact]
    public async Task RejectsAManualCollisionWithoutCreatingAnotherRun()
    {
        var gateway = new BlockingArtifactExecutionGateway();
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString, gateway);
        using var client = application.CreateClient();
        await SaveCatalogAsync(client, Guid.NewGuid());

        using var firstResponse = await client.PostAsync("/api/runs", null);
        using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStreamAsync());
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var collisionResponse = await client.PostAsync("/api/runs", null);
        using var collision = JsonDocument.Parse(await collisionResponse.Content.ReadAsStreamAsync());
        using var historyResponse = await client.GetAsync("/api/runs");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, collisionResponse.StatusCode);
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), collision.RootElement.GetProperty("activeRun").GetProperty("id").GetGuid());
        Assert.Single(history.RootElement.EnumerateArray());

        gateway.Complete.TrySetResult();
        await WaitForRunAsync(client, first.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RedactsConfiguredSecretsFromPersistedRunLogs()
    {
        const string secret = "super-secret-value";
        var gateway = new ThrowingArtifactExecutionGateway(
            new InvalidOperationException($"Failed with {secret} at https://user:password@example.test/source."));
        await using var application = new RegistryBridgeApplicationFactory(
            postgreSql.ConnectionString,
            gateway,
            new Dictionary<string, string?> { ["Deployment:SecretValues:0"] = secret });
        using var client = application.CreateClient();
        await SaveCatalogAsync(client, Guid.NewGuid());

        using var startResponse = await client.PostAsync("/api/runs", null);
        using var started = JsonDocument.Parse(await startResponse.Content.ReadAsStreamAsync());
        using var completed = await WaitForRunAsync(client, started.RootElement.GetProperty("id").GetGuid());
        var logs = completed.RootElement.GetProperty("logs").GetRawText();

        Assert.Equal("Failed", completed.RootElement.GetProperty("outcomes")[0].GetProperty("disposition").GetString());
        Assert.DoesNotContain(secret, logs);
        Assert.DoesNotContain("user:password", logs);
        Assert.Contains("[REDACTED]", logs);
    }

    [Fact]
    public async Task IdentifiesWhenPersistedRunLogsReachTheConfiguredLimit()
    {
        await using var application = new RegistryBridgeApplicationFactory(
            postgreSql.ConnectionString,
            new SequencedArtifactExecutionGateway(
                new ArtifactOutcome(ArtifactOutcomeDisposition.Promoted)),
            new Dictionary<string, string?> { ["Deployment:RunLogLimitBytes"] = "1" });
        using var client = application.CreateClient();
        await SaveCatalogAsync(client, Guid.NewGuid());

        using var startResponse = await client.PostAsync("/api/runs", null);
        using var started = JsonDocument.Parse(await startResponse.Content.ReadAsStreamAsync());
        using var completed = await WaitForRunAsync(client, started.RootElement.GetProperty("id").GetGuid());

        Assert.True(completed.RootElement.GetProperty("logsTruncated").GetBoolean());
    }

    private static async Task<JsonDocument> WaitForRunAsync(HttpClient client, Guid runId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync($"/api/runs/{runId}");
            var body = await response.Content.ReadAsStringAsync();
            var document = JsonDocument.Parse(body);
            if (document.RootElement.GetProperty("status").GetString() is "Completed" or "Abandoned")
            {
                return document;
            }

            document.Dispose();
            await Task.Delay(25);
        }

        throw new TimeoutException("The Synchronization Run did not complete.");
    }

    private static async Task SaveCatalogAsync(HttpClient client, params Guid[] entryIds)
    {
        var entries = entryIds.Select(
                (id, index) => new
                {
                    id,
                    sourceReference = $"registry.example.test/team/widget-{index}@sha256:{new string((char)('a' + index), 64)}",
                    targetRepository = $"mirrors/team/widget-{index}",
                    targetTag = "2026.08.07",
                    credentialHandle = (string?)null,
                    enabled = index != 1
                })
            .ToArray();
        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new { baseRevisionId = (Guid?)null, entries });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var scope = application.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();
        var catalogState = await database.CatalogStates.SingleAsync();
        catalogState.CurrentRevisionId = null;
        await database.SaveChangesAsync();
        await database.SynchronizationRuns.ExecuteDeleteAsync();
        await database.CatalogRevisions.ExecuteDeleteAsync();
    }
}

public sealed class SequencedArtifactExecutionGateway(params ArtifactOutcome[] outcomes) : IArtifactExecutionGateway
{
    private readonly Queue<ArtifactOutcome> outcomes = new(outcomes);

    public List<ArtifactExecutionRequest> Requests { get; } = [];

    public Task<ArtifactOutcome> ExecuteAsync(ArtifactExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(outcomes.Dequeue());
    }
}

public sealed class BlockingArtifactExecutionGateway : IArtifactExecutionGateway
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Complete.Task.WaitAsync(cancellationToken);
        return new ArtifactOutcome(ArtifactOutcomeDisposition.Promoted);
    }
}

public sealed class ThrowingArtifactExecutionGateway(Exception exception) : IArtifactExecutionGateway
{
    public Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<ArtifactOutcome>(exception);
}
