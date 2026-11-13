using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Carts.Api.Persistence;

/// <summary>
/// Ready means the database answers and has the schema this build needs. The probe has its
/// own small connection pool with short limits. A database that accepts the connection and
/// then says nothing is reported within seconds, not after the driver's default timeouts,
/// and a burst of calls to this public endpoint can never open more than two database
/// connections.
/// </summary>
public sealed class DatabaseHealthCheck(string connectionString, string? latestMigrationId) : IHealthCheck
{
    /// <summary>The name the probe's connections carry in the database's own session list.</summary>
    public const string ApplicationName = "carts-readiness";

    private readonly string _probeConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = ApplicationName,
        Timeout = 2,
        CommandTimeout = 2,
        CancellationTimeout = 1000,
        MinPoolSize = 0,
        MaxPoolSize = 2,
    }.ConnectionString;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // A database that cannot be asked throws here. The health service logs the exception
        // and answers "Unhealthy"; nothing about the failure reaches the caller.
        await using var connection = new NpgsqlConnection(_probeConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = @id""",
            connection);
        command.Parameters.AddWithValue("id", latestMigrationId ?? string.Empty);

        return await command.ExecuteScalarAsync(cancellationToken) is null
            ? HealthCheckResult.Unhealthy("The newest migration of this build has not been applied.")
            : HealthCheckResult.Healthy();
    }
}
