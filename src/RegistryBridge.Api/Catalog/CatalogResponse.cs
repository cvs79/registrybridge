namespace RegistryBridge.Api.Catalog;

public sealed record CatalogResponse(
    CatalogRevisionResponse? CurrentRevision,
    IReadOnlyList<CatalogEntryResponse> Entries,
    IReadOnlyList<VulnerabilityExceptionResponse>? VulnerabilityExceptions = null);

public sealed record CatalogRevisionResponse(Guid Id, DateTimeOffset CreatedAt);

public sealed record CatalogRevisionSnapshotResponse(
    CatalogRevisionResponse Revision,
    IReadOnlyList<CatalogEntryResponse> Entries,
    IReadOnlyList<VulnerabilityExceptionResponse>? VulnerabilityExceptions = null);

public sealed record CatalogEntryResponse(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled,
    int Order,
    CatalogEntryKind Kind = CatalogEntryKind.ImageMirror,
    string? SourceVersion = null,
    string? ExpectedDigest = null);

public sealed record VulnerabilityExceptionResponse(
    Guid Id,
    string ImageDigest,
    IReadOnlyList<string> VulnerabilityIds,
    string Reason);

public sealed record CatalogSaveRequest(
    Guid? BaseRevisionId,
    IReadOnlyList<CatalogEntryInput> Entries,
    IReadOnlyList<VulnerabilityExceptionInput>? VulnerabilityExceptions = null);

public sealed record CatalogEntryInput(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled,
    CatalogEntryKind Kind = CatalogEntryKind.ImageMirror,
    string? SourceVersion = null,
    string? ExpectedDigest = null);

public sealed record VulnerabilityExceptionInput(
    Guid Id,
    string ImageDigest,
    IReadOnlyList<string> VulnerabilityIds,
    string Reason);

public sealed record CatalogSaveConflictResponse(Guid? CurrentRevisionId, string? Reason = null);
