using System.Text.Json;
using System.Text.Json.Serialization;

namespace RegistryBridge.Api.Catalog;

public sealed record CatalogDefinition(
    IReadOnlyList<CatalogEntryDefinition> Entries,
    IReadOnlyList<VulnerabilityExceptionDefinition>? VulnerabilityExceptions = null);

public sealed record CatalogEntryDefinition(
    Guid Id,
    string SourceReference,
    string TargetRepository,
    string TargetTag,
    string? CredentialHandle,
    bool Enabled,
    CatalogEntryKind Kind = CatalogEntryKind.ImageMirror,
    string? SourceVersion = null,
    string? ExpectedDigest = null);

public sealed record VulnerabilityExceptionDefinition(
    Guid Id,
    string ImageDigest,
    IReadOnlyList<string> VulnerabilityIds,
    string Reason);

public enum CatalogEntryKind
{
    ImageMirror,
    WrapperBuild,
    HelmChart
}

public static class CatalogDefinitions
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static CatalogDefinition Deserialize(string definition) =>
        JsonSerializer.Deserialize<CatalogDefinition>(definition, SerializationOptions)
        ?? throw new InvalidOperationException("A Catalog Revision has an invalid definition.");

    public static string Serialize(CatalogDefinition definition) =>
        JsonSerializer.Serialize(definition, SerializationOptions);

    public static bool IsEquivalent(CatalogDefinition left, CatalogDefinition right) =>
        left.Entries.Count == right.Entries.Count
        && left.Entries.SequenceEqual(right.Entries)
        && (left.VulnerabilityExceptions ?? [])
            .SequenceEqual(right.VulnerabilityExceptions ?? []);

    public static CatalogResponse ToResponse(CatalogRevision? revision)
    {
        if (revision is null)
        {
            return new CatalogResponse(
                null,
                Array.Empty<CatalogEntryResponse>(),
                Array.Empty<VulnerabilityExceptionResponse>());
        }

        var definition = Deserialize(revision.Definition);
        return new CatalogResponse(
            new CatalogRevisionResponse(revision.Id, revision.CreatedAt),
            ToEntryResponses(definition),
            ToVulnerabilityExceptionResponses(definition));
    }

    public static CatalogRevisionSnapshotResponse ToSnapshotResponse(CatalogRevision revision) =>
        new(
            new CatalogRevisionResponse(revision.Id, revision.CreatedAt),
            ToEntryResponses(Deserialize(revision.Definition)),
            ToVulnerabilityExceptionResponses(Deserialize(revision.Definition)));

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
                    index,
                    entry.Kind,
                    entry.SourceVersion,
                    entry.ExpectedDigest))
            .ToArray();

    private static IReadOnlyList<VulnerabilityExceptionResponse> ToVulnerabilityExceptionResponses(
        CatalogDefinition definition) =>
        (definition.VulnerabilityExceptions ?? [])
        .Select(
            exception => new VulnerabilityExceptionResponse(
                exception.Id,
                exception.ImageDigest,
                exception.VulnerabilityIds,
                exception.Reason))
        .ToArray();
}
