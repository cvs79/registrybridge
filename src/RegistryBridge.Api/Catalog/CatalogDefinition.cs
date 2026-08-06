using System.Text.Json;

namespace RegistryBridge.Api.Catalog;

public sealed record CatalogDefinition(IReadOnlyList<CatalogEntryDefinition> Entries);

public sealed record CatalogEntryDefinition(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled);

public static class CatalogDefinitions
{
    public static CatalogDefinition Deserialize(string definition) =>
        JsonSerializer.Deserialize<CatalogDefinition>(definition)
        ?? throw new InvalidOperationException("A Catalog Revision has an invalid definition.");

    public static string Serialize(CatalogDefinition definition) =>
        JsonSerializer.Serialize(definition);

    public static bool IsEquivalent(CatalogDefinition left, CatalogDefinition right) =>
        left.Entries.Count == right.Entries.Count
        && left.Entries.SequenceEqual(right.Entries);

    public static CatalogResponse ToResponse(CatalogRevision? revision)
    {
        if (revision is null)
        {
            return new CatalogResponse(null, Array.Empty<CatalogEntryResponse>());
        }

        var definition = Deserialize(revision.Definition);
        return new CatalogResponse(
            new CatalogRevisionResponse(revision.Id, revision.CreatedAt),
            ToEntryResponses(definition));
    }

    public static CatalogRevisionSnapshotResponse ToSnapshotResponse(CatalogRevision revision) =>
        new(
            new CatalogRevisionResponse(revision.Id, revision.CreatedAt),
            ToEntryResponses(Deserialize(revision.Definition)));

    private static IReadOnlyList<CatalogEntryResponse> ToEntryResponses(CatalogDefinition definition) =>
        definition.Entries
            .Select(
                (entry, index) => new CatalogEntryResponse(
                    entry.Id,
                    entry.SourceReference,
                    entry.TargetRepository,
                    entry.TargetTag,
                    entry.CredentialHandle,
                    entry.Enabled,
                    index))
            .ToArray();
}
