using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RegistryBridge.Api.ArtifactExecution;
using Testcontainers.PostgreSql;

namespace RegistryBridge.Api.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("registrybridge_tests")
        .WithUsername("registrybridge")
        .WithPassword("registrybridge")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ControlPlaneIntegrationTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task PresentsAnEmptyCatalogForANewInstallation()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/api/catalog");
        using var catalog = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, catalog.RootElement.GetProperty("currentRevision").ValueKind);
        Assert.Empty(catalog.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task ReportsHealthyAndReadyAfterMigratingTheDatabase()
    {
        await using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString);
        using var client = application.CreateClient();

        using var health = await client.GetAsync("/health");
        using var aliveness = await client.GetAsync("/alive");
        using var healthz = await client.GetAsync("/healthz");
        using var readyz = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliveness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthz.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyz.StatusCode);
    }

    [Fact]
    public void AllowsArtifactExecutionGatewayToBeReplacedAtTheApplicationBoundary()
    {
        var gateway = new TestArtifactExecutionGateway();
        using var application = new RegistryBridgeApplicationFactory(postgreSql.ConnectionString, gateway);

        using var scope = application.Services.CreateScope();
        var registeredGateway = scope.ServiceProvider.GetRequiredService<IArtifactExecutionGateway>();

        Assert.Same(gateway, registeredGateway);
    }
}

public sealed class RegistryBridgeApplicationFactory(
    string connectionString,
    IArtifactExecutionGateway? artifactExecutionGateway = null,
    IReadOnlyDictionary<string, string?>? configuration = null) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RegistryBridge", connectionString);
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            if (configuration is not null)
            {
                configurationBuilder.AddInMemoryCollection(configuration);
            }
        });

        if (artifactExecutionGateway is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IArtifactExecutionGateway>();
                services.AddSingleton(artifactExecutionGateway);
            });
        }
    }
}

public sealed class TestArtifactExecutionGateway : IArtifactExecutionGateway
{
    public Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ArtifactOutcome(ArtifactOutcomeDisposition.Promoted));
}
