namespace RegistryBridge.Api.Catalog;

public sealed record CatalogResponse(
    CatalogRevisionResponse? CurrentRevision,
    IReadOnlyList<CatalogEntryResponse> Entries);

public sealed record CatalogRevisionResponse(Guid Id, DateTimeOffset CreatedAt);

public sealed record CatalogRevisionSnapshotResponse(
    CatalogRevisionResponse Revision,
    IReadOnlyList<CatalogEntryResponse> Entries);

public sealed record CatalogEntryResponse(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled,
    int Order);

public sealed record CatalogSaveRequest(
    Guid? BaseRevisionId,
    IReadOnlyList<CatalogEntryInput> Entries);

public sealed record CatalogEntryInput(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled);

public sealed record CatalogSaveConflictResponse(Guid? CurrentRevisionId, string? Reason = null);
