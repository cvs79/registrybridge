var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var database = postgres.AddDatabase("registrybridge");

var api = builder.AddProject<Projects.RegistryBridge_Api>("api")
    .WithReference(database)
    .WaitFor(database);

builder.AddNpmApp("web", "../web", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .PublishAsDockerFile();

builder.Build().Run();
