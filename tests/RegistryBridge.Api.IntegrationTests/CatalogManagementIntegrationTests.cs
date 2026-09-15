using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegistryBridge.Api.Data;

namespace RegistryBridge.Api.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogManagementIntegrationTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    public Task InitializeAsync() => ResetCatalogAsync();

    public Task DisposeAsync() => ResetCatalogAsync();

    [Fact]
    public async Task CreatesAnImmutableCatalogRevisionForAnImageMirrorEntry()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var catalog = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(entryId.ToString(), catalog.RootElement.GetProperty("entries")[0].GetProperty("id").GetString());
        Assert.Equal(
            "mirrors/team/widget",
            catalog.RootElement.GetProperty("entries")[0].GetProperty("targetRepository").GetString());
        Assert.Equal(0, catalog.RootElement.GetProperty("entries")[0].GetProperty("order").GetInt32());
        Assert.Equal(JsonValueKind.String, catalog.RootElement.GetProperty("currentRevision").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task RejectsAnImageMirrorWithoutASha256SourceDigest()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference = "registry.example.test/team/widget:latest",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "entries[0].sourceReference",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task RejectsActiveEntriesThatShareATargetRepository()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/first@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/shared",
                        targetTag = "first",
                        credentialHandle = (string?)null,
                        enabled = true
                    },
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/second@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        targetRepository = "mirrors/team/shared",
                        targetTag = "second",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "entries[1].targetRepository",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task PresentsDeclaredCredentialHandlesWithoutCredentialValues()
    {
        await using var application = new RegistryBridgeApplicationFactory(
            postgreSql.ConnectionString,
            configuration: new Dictionary<string, string?>
            {
                ["Deployment:TargetRegistry"] = "registry.example.test",
                ["Deployment:RunLogLimitBytes"] = "5242880",
                ["Deployment:CredentialHandles:0:Name"] = "upstream-registry",
                ["Deployment:CredentialHandles:0:Type"] = "OciRegistry",
                ["CredentialValues:upstream-registry"] = "do-not-expose-me"
            });
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/api/deployment-configuration");
        using var configuration = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("registry.example.test", configuration.RootElement.GetProperty("targetRegistry").GetString());
        Assert.Equal(5_242_880, configuration.RootElement.GetProperty("runLogLimitBytes").GetInt32());
        Assert.Equal(
            "upstream-registry",
            configuration.RootElement.GetProperty("credentialHandles")[0].GetProperty("name").GetString());
        Assert.Equal(
            "OciRegistry",
            configuration.RootElement.GetProperty("credentialHandles")[0].GetProperty("type").GetString());
        Assert.DoesNotContain("do-not-expose-me", configuration.RootElement.GetRawText());
    }

    [Fact]
    public async Task RejectsAnUndeclaredCredentialHandle()
    {
        await using var application = new RegistryBridgeApplicationFactory(
            postgreSql.ConnectionString,
            configuration: new Dictionary<string, string?>
            {
                ["Deployment:CredentialHandles:0:Name"] = "upstream-registry",
                ["Deployment:CredentialHandles:0:Type"] = "OciRegistry"
            });
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = "unknown-handle",
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "entries[0].credentialHandle",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task RejectsACredentialHandleWithAnIncompatibleType()
    {
        await using var application = new RegistryBridgeApplicationFactory(
            postgreSql.ConnectionString,
            configuration: new Dictionary<string, string?>
            {
                ["Deployment:CredentialHandles:0:Name"] = "git-source",
                ["Deployment:CredentialHandles:0:Type"] = "HttpsGit"
            });
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = "git-source",
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "entries[0].credentialHandle",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task RejectsMalformedTargetRepositoryAndTargetTag()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "Mirrors/team/widget",
                        targetTag = "release tag",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var errorProperties = problem.RootElement.GetProperty("errors")
            .EnumerateObject()
            .Select(property => property.Name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("entries[0].targetRepository", errorProperties);
        Assert.Contains("entries[0].targetTag", errorProperties);
    }

    [Fact]
    public async Task RetainsAnImmutableSnapshotForEachCatalogRevision()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();

        using var firstSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var firstCatalog = JsonDocument.Parse(await firstSave.Content.ReadAsStreamAsync());
        var firstRevisionId = firstCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        using var secondSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = firstRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.07",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var secondCatalog = JsonDocument.Parse(await secondSave.Content.ReadAsStreamAsync());

        using var historyResponse = await client.GetAsync("/api/catalog/revisions");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStreamAsync());
        using var firstRevisionResponse = await client.GetAsync($"/api/catalog/revisions/{firstRevisionId}");
        using var firstRevision = JsonDocument.Parse(await firstRevisionResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, firstSave.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondSave.StatusCode);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstRevisionResponse.StatusCode);
        Assert.Equal(2, history.RootElement.GetArrayLength());
        Assert.Equal(
            secondCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid(),
            history.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(
            "2026.08.06",
            firstRevision.RootElement.GetProperty("entries")[0].GetProperty("targetTag").GetString());
        Assert.Equal(
            firstRevisionId,
            firstRevision.RootElement.GetProperty("revision").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RejectsANoOpCatalogSaveWithoutCreatingAnotherRevision()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();
        var entry = new
        {
            id = entryId,
            sourceReference =
                "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            targetRepository = "mirrors/team/widget",
            targetTag = "2026.08.06",
            credentialHandle = (string?)null,
            enabled = true
        };

        using var firstSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new { baseRevisionId = (Guid?)null, entries = new[] { entry } });
        using var firstCatalog = JsonDocument.Parse(await firstSave.Content.ReadAsStreamAsync());
        var revisionId = firstCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        using var noOpSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new { baseRevisionId = revisionId, entries = new[] { entry } });
        using var historyResponse = await client.GetAsync("/api/catalog/revisions");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, firstSave.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noOpSave.StatusCode);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(1, history.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task RejectsAWriteBasedOnANonCurrentCatalogRevision()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();

        using var firstSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var firstCatalog = JsonDocument.Parse(await firstSave.Content.ReadAsStreamAsync());
        var firstRevisionId = firstCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        using var currentSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = firstRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.07",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var staleSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = firstRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.08",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var historyResponse = await client.GetAsync("/api/catalog/revisions");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, firstSave.StatusCode);
        Assert.Equal(HttpStatusCode.Created, currentSave.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleSave.StatusCode);
        Assert.Equal(2, history.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task AcceptsOnlyOneOfTwoConcurrentCatalogWrites()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();

        using var initialSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var initialCatalog = JsonDocument.Parse(await initialSave.Content.ReadAsStreamAsync());
        var baseRevisionId = initialCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        var firstWrite = client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.07",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        var secondWrite = client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "2026.08.08",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });

        var writes = await Task.WhenAll(firstWrite, secondWrite);
        using var historyResponse = await client.GetAsync("/api/catalog/revisions");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, initialSave.StatusCode);
        Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, history.RootElement.GetArrayLength());

        foreach (var response in writes)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task RejectsRemappingATargetTagToADifferentDigest()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var entryId = Guid.NewGuid();

        using var initialSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "stable",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var initialCatalog = JsonDocument.Parse(await initialSave.Content.ReadAsStreamAsync());
        var baseRevisionId = initialCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        using var remappingSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId,
                entries = new[]
                {
                    new
                    {
                        id = entryId,
                        sourceReference =
                            "registry.example.test/team/widget@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "stable",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var problem = JsonDocument.Parse(await remappingSave.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, initialSave.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, remappingSave.StatusCode);
        Assert.Contains(
            "entries[0].targetTag",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task RejectsMappingOneTargetTagToDifferentDigestsInTheSameCatalogSave()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/first@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "stable",
                        credentialHandle = (string?)null,
                        enabled = true
                    },
                    new
                    {
                        id = Guid.NewGuid(),
                        sourceReference =
                            "registry.example.test/team/second@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        targetRepository = "mirrors/team/widget",
                        targetTag = "stable",
                        credentialHandle = (string?)null,
                        enabled = false
                    }
                }
            });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "entries[1].targetTag",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task PreservesOperatorOrderingAndDisabledEntries()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        var firstEntryId = Guid.NewGuid();
        var secondEntryId = Guid.NewGuid();

        using var initialSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = (Guid?)null,
                entries = new[]
                {
                    new
                    {
                        id = firstEntryId,
                        sourceReference =
                            "registry.example.test/team/first@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/first",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = false
                    },
                    new
                    {
                        id = secondEntryId,
                        sourceReference =
                            "registry.example.test/team/second@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        targetRepository = "mirrors/team/second",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    }
                }
            });
        using var initialCatalog = JsonDocument.Parse(await initialSave.Content.ReadAsStreamAsync());
        var revisionId = initialCatalog.RootElement.GetProperty("currentRevision").GetProperty("id").GetGuid();

        using var reorderedSave = await client.PutAsJsonAsync(
            "/api/catalog",
            new
            {
                baseRevisionId = revisionId,
                entries = new[]
                {
                    new
                    {
                        id = secondEntryId,
                        sourceReference =
                            "registry.example.test/team/second@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        targetRepository = "mirrors/team/second",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = true
                    },
                    new
                    {
                        id = firstEntryId,
                        sourceReference =
                            "registry.example.test/team/first@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        targetRepository = "mirrors/team/first",
                        targetTag = "2026.08.06",
                        credentialHandle = (string?)null,
                        enabled = false
                    }
                }
            });
        using var catalogResponse = await client.GetAsync("/api/catalog");
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, initialSave.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reorderedSave.StatusCode);
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        Assert.Equal(secondEntryId, catalog.RootElement.GetProperty("entries")[0].GetProperty("id").GetGuid());
        Assert.True(catalog.RootElement.GetProperty("entries")[0].GetProperty("enabled").GetBoolean());
        Assert.Equal(firstEntryId, catalog.RootElement.GetProperty("entries")[1].GetProperty("id").GetGuid());
        Assert.False(catalog.RootElement.GetProperty("entries")[1].GetProperty("enabled").GetBoolean());
        Assert.Equal(1, catalog.RootElement.GetProperty("entries")[1].GetProperty("order").GetInt32());
    }

    private async Task ResetCatalogAsync()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();
        using var scope = application.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RegistryBridgeDbContext>();

        var catalogState = await database.CatalogStates.SingleAsync();
        catalogState.CurrentRevisionId = null;
        await database.SaveChangesAsync();
        await database.CatalogRevisions.ExecuteDeleteAsync();
    }
}
