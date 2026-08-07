namespace RegistryBridge.Api.SynchronizationRuns;

public sealed record SynchronizationRunResponse(
    Guid Id,
    Guid? CatalogRevisionId,
    string Origin,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record SynchronizationRunDetailResponse(
    Guid Id,
    Guid? CatalogRevisionId,
    string Origin,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ArtifactOutcomeResponse> Outcomes,
    IReadOnlyList<RunLogResponse> Logs);

public sealed record ArtifactOutcomeResponse(
    Guid CatalogEntryId,
    int Order,
    string Disposition,
    string? Detail);

public sealed record RunLogResponse(
    DateTimeOffset OccurredAt,
    string EventType,
    string Data);

public sealed record ManualSynchronizationRunConflictResponse(SynchronizationRunResponse ActiveRun);

public static class SynchronizationRunResponses
{
    public static SynchronizationRunResponse ToResponse(SynchronizationRun run) =>
        new(
            run.Id,
            run.CatalogRevisionId,
            run.Origin.ToString(),
            run.Status.ToString(),
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt);

    public static SynchronizationRunDetailResponse ToDetailResponse(SynchronizationRun run) =>
        new(
            run.Id,
            run.CatalogRevisionId,
            run.Origin.ToString(),
            run.Status.ToString(),
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.Outcomes
                .OrderBy(outcome => outcome.Order)
                .Select(
                    outcome => new ArtifactOutcomeResponse(
                        outcome.CatalogEntryId,
                        outcome.Order,
                        outcome.Disposition == ArtifactOutcomeDisposition.NotSelected
                            ? "Not selected"
                            : outcome.Disposition.ToString(),
                        outcome.Detail))
                .ToArray(),
            run.Logs
                .OrderBy(log => log.OccurredAt)
                .Select(log => new RunLogResponse(log.OccurredAt, log.EventType, log.Data))
                .ToArray());
}
