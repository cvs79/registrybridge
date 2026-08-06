namespace RegistryBridge.Api.Deployment;

public sealed class DeploymentConfiguration
{
    public string TargetRegistry { get; init; } = string.Empty;

    public List<DeclaredCredentialHandle> CredentialHandles { get; init; } = [];
}

public sealed class DeclaredCredentialHandle
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;
}

public sealed record DeploymentConfigurationResponse(
    string TargetRegistry,
    IReadOnlyList<CredentialHandleResponse> CredentialHandles);

public sealed record CredentialHandleResponse(string Name, string Type);
