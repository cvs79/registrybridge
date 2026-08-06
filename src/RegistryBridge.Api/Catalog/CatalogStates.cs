using Microsoft.EntityFrameworkCore;
using RegistryBridge.Api.Data;

namespace RegistryBridge.Api.Catalog;

public static class CatalogStates
{
    public static Task<CatalogState> LoadAsync(
        RegistryBridgeDbContext database,
        CancellationToken cancellationToken) =>
        database.CatalogStates
            .Include(state => state.CurrentRevision)
            .SingleAsync(state => state.Id == CatalogState.SingletonId, cancellationToken);
}
