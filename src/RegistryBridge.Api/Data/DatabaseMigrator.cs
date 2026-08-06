using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RegistryBridge.Api.Data;

public sealed class DatabaseMigrator(RegistryBridgeDbContext database)
{
    private const long MigrationLockId = 772320949489648949;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var connectionString = database.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The RegistryBridge database connection string is required.");

        await using var migrationLockConnection = new NpgsqlConnection(connectionString);
        await migrationLockConnection.OpenAsync(cancellationToken);

        await using var acquireLock = new NpgsqlCommand(
            "SELECT pg_advisory_lock(@lock_id);",
            migrationLockConnection);
        acquireLock.Parameters.AddWithValue("lock_id", MigrationLockId);
        await acquireLock.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await database.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await using var releaseLock = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@lock_id);",
                migrationLockConnection);
            releaseLock.Parameters.AddWithValue("lock_id", MigrationLockId);
            await releaseLock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
