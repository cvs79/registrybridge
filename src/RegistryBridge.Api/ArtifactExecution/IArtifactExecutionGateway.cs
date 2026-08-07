namespace RegistryBridge.Api.ArtifactExecution;

public interface IArtifactExecutionGateway
{
    Task<ArtifactOutcome> ExecuteAsync(
        ArtifactExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ArtifactExecutionRequest(
    Guid CatalogEntryId,
    string TargetRepository,
    string TargetTag,
    string SourceReference)
{
    public ArtifactExecutionRequest(string targetRepository, string sourceReference)
        : this(Guid.Empty, targetRepository, string.Empty, sourceReference)
    {
    }
}

public sealed record ArtifactOutcome(
    ArtifactOutcomeDisposition Disposition,
    string? Detail = null);

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
            "Artifact execution is not configured for this deployment.");
}
