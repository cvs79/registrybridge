namespace RegistryBridge.Api.Catalog;

public sealed record CatalogResponse(
    CatalogRevisionResponse? CurrentRevision,
    IReadOnlyList<CatalogEntryResponse> Entries);

public sealed record CatalogRevisionResponse(Guid Id, DateTimeOffset CreatedAt);

public sealed record CatalogEntryResponse;
