using Carts.Api.Products;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Persistence;

/// <summary>Brings a database up to date. It runs as its own step, never as part of serving requests.</summary>
public static class DatabaseSetup
{
    public static async Task RunAsync(CartDbContext db, bool seedDemoProducts, CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (seedDemoProducts && !await db.Products.AnyAsync(cancellationToken))
        {
            // One transaction, so an interrupted run leaves no partial catalog behind. Two
            // setups started at the same moment may both find the table empty; the second
            // insert of a product is then skipped instead of failing.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var product in DemoProducts.Create())
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO products (sku, name, unit_price, currency, is_active)
                    VALUES ({product.Sku}, {product.Name}, {product.UnitPrice}, {product.Currency}, {product.IsActive})
                    ON CONFLICT (sku) DO NOTHING
                    """,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }
}
