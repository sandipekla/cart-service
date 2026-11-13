using Carts.Api.Persistence;
using Carts.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Carts.IntegrationTests;

/// <summary>One PostgreSQL container for the whole test run, with the migrations applied.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public CartDbContext CreateContext() => CreateContext(ConnectionString);

    public static CartDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CartDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>Creates a database with no tables in it and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"empty_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}
