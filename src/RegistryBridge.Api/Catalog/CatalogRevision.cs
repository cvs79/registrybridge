namespace RegistryBridge.Api.Catalog;

public sealed class CatalogRevision
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Definition { get; set; } = "{}";
}
