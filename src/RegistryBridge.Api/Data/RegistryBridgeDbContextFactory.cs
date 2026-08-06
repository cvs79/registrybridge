using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RegistryBridge.Api.Data;

public sealed class RegistryBridgeDbContextFactory : IDesignTimeDbContextFactory<RegistryBridgeDbContext>
{
    public RegistryBridgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RegistryBridgeDbContext>()
            .UseNpgsql("Host=localhost;Database=registrybridge")
            .Options;

        return new RegistryBridgeDbContext(options);
    }
}
