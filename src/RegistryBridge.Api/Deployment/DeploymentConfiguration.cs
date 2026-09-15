namespace RegistryBridge.Api.Deployment;

public sealed class DeploymentConfiguration
{
    public string TargetRegistry { get; init; } = string.Empty;

    public List<DeclaredCredentialHandle> CredentialHandles { get; init; } = [];

    public List<string> SecretValues { get; init; } = [];

    public int RetentionDays { get; init; } = 90;

    public int RunLogLimitBytes { get; init; } = 10 * 1024 * 1024;
}

public sealed class DeclaredCredentialHandle
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;
}

public sealed record DeploymentConfigurationResponse(
    string TargetRegistry,
    int RunLogLimitBytes,
    IReadOnlyList<CredentialHandleResponse> CredentialHandles);

public sealed record CredentialHandleResponse(string Name, string Type);
