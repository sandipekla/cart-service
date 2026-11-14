using System.Reflection;
using Carts.Api.Health;
using Carts.Api.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Carts.Api.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Carts";

    public static IServiceCollection AddCartPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
        }

        // A failed query is logged where its error is handled, not here as well. Only this
        // event is turned down: the category also carries warnings worth seeing.
        services.AddDbContext<CartDbContext>(options => options
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Log((CoreEventId.QueryIterationFailed, LogLevel.Debug))));
        services.AddScoped<ProductCatalog>();

        // The probe gives up by itself after a few seconds; this timeout is only a backstop.
        services.AddHealthChecks().AddCheck(
            "database",
            new DatabaseHealthCheck(connectionString, LatestMigrationId()),
            tags: [HealthEndpoints.ReadyTag],
            timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>
    /// Migration ids start with a timestamp, so the greatest one is the newest. Null only
    /// while the very first migration is being created.
    /// </summary>
    private static string? LatestMigrationId() =>
        typeof(CartDbContext).Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .Max(StringComparer.Ordinal);
}
