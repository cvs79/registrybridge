namespace RegistryBridge.Api.ArtifactExecution;

public interface IArtifactExecutionGateway
{
    Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ArtifactExecutionRequest(string TargetRepository, string SourceReference);

public sealed record ArtifactOutcome(ArtifactOutcomeDisposition Disposition);

public enum ArtifactOutcomeDisposition
{
    Promoted,
    Verified,
    Blocked,
    Conflict,
    Failed
}

public sealed class UnavailableArtifactExecutionGateway : IArtifactExecutionGateway
{
    public Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Artifact execution is not available until a Synchronization Run is implemented.");
}
