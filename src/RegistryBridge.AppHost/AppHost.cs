var environmentFile = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env")
    }
    .Select(Path.GetFullPath)
    .FirstOrDefault(File.Exists);

if (environmentFile is not null)
{
    DotNetEnv.Env.Load(environmentFile);
}

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var database = postgres.AddDatabase("registrybridge");

var api = builder.AddProject<Projects.RegistryBridge_Api>("api")
    .WithReference(database)
    .WaitFor(database);

var targetRegistry = Environment.GetEnvironmentVariable("REGISTRYBRIDGE_TARGET_REGISTRY")
    ?? builder.Configuration["Deployment:TargetRegistry"];
var credentialHandleName = Environment.GetEnvironmentVariable("REGISTRYBRIDGE_CREDENTIAL_HANDLE_NAME")
    ?? builder.Configuration["Deployment:CredentialHandles:0:Name"];
var credentialHandleType = Environment.GetEnvironmentVariable("REGISTRYBRIDGE_CREDENTIAL_HANDLE_TYPE")
    ?? builder.Configuration["Deployment:CredentialHandles:0:Type"];

if (!string.IsNullOrWhiteSpace(targetRegistry))
{
    api.WithEnvironment("Deployment__TargetRegistry", targetRegistry);
}

if (!string.IsNullOrWhiteSpace(credentialHandleName))
{
    api.WithEnvironment("Deployment__CredentialHandles__0__Name", credentialHandleName);
}

if (!string.IsNullOrWhiteSpace(credentialHandleType))
{
    api.WithEnvironment("Deployment__CredentialHandles__0__Type", credentialHandleType);
}

builder.AddNpmApp("web", "../../web", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .PublishAsDockerFile();

builder.Build().Run();
