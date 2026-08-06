namespace RegistryBridge.Api.Catalog;

public sealed class CatalogState
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    public Guid? CurrentRevisionId { get; set; }

    public CatalogRevision? CurrentRevision { get; set; }
}
