namespace RegistryBridge.Api.SynchronizationRuns;

public sealed class SynchronizationRun
{
    public Guid Id { get; set; }

    public Guid? CatalogRevisionId { get; set; }

    public SynchronizationRunOrigin Origin { get; set; }

    public SynchronizationRunStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Catalog.CatalogRevision? CatalogRevision { get; set; }

    public List<ArtifactOutcomeRecord> Outcomes { get; set; } = [];

    public List<RunLog> Logs { get; set; } = [];
}

public sealed class ArtifactOutcomeRecord
{
    public Guid Id { get; set; }

    public Guid SynchronizationRunId { get; set; }

    public Guid CatalogEntryId { get; set; }

    public int Order { get; set; }

    public ArtifactOutcomeDisposition Disposition { get; set; }

    public string? Detail { get; set; }

    public SynchronizationRun? SynchronizationRun { get; set; }
}

public sealed class RunLog
{
    public Guid Id { get; set; }

    public Guid SynchronizationRunId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Data { get; set; } = "{}";

    public SynchronizationRun? SynchronizationRun { get; set; }
}

public enum SynchronizationRunOrigin
{
    Manual,
    Scheduled
}

public enum SynchronizationRunStatus
{
    Running,
    Completed,
    Skipped,
    Abandoned
}

public enum ArtifactOutcomeDisposition
{
    Promoted,
    Verified,
    Blocked,
    Conflict,
    Failed,
    NotSelected
}
