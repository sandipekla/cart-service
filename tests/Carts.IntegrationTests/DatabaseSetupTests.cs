using Carts.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Carts.IntegrationTests;

public class DatabaseSetupTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setup_with_demo_data_creates_the_schema_and_a_catalog_with_an_inactive_and_a_foreign_currency_product()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.CreateEmptyDatabaseAsync());

        await DatabaseSetup.RunAsync(db, seedDemoProducts: true, Cancellation);

        // The README's walkthrough relies on exactly these three.
        var products = await db.Products.AsNoTracking().ToListAsync(Cancellation);
        Assert.Equal(8, products.Count);
        Assert.Contains(products, product => product.Sku == "TSHIRT-BLK-M" && product.IsActive && product.Currency == "EUR");
        Assert.Contains(products, product => product.Sku == "POSTER-2019" && !product.IsActive);
        Assert.Contains(products, product => product.Sku == "GIFTCARD-USD-25" && product.IsActive && product.Currency == "USD");
        Assert.All(products, product => Assert.NotNull(product.ToSnapshot()));
    }

    [Fact]
    public async Task Setup_started_several_times_at_once_ends_with_one_schema_and_one_catalog()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = PostgresFixture.CreateContext(connectionString);
            await DatabaseSetup.RunAsync(db, seedDemoProducts: true, Cancellation);
        }));

        await using var check = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(8, await check.Products.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Setup_without_demo_data_adds_no_products_and_demo_data_never_joins_products_already_there()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.CreateEmptyDatabaseAsync());
        await DatabaseSetup.RunAsync(db, seedDemoProducts: false, Cancellation);
        Assert.Equal(0, await db.Products.CountAsync(Cancellation));
        db.Products.Add(new("REAL-1", "A real product", 1m, "EUR", isActive: true));
        await db.SaveChangesAsync(Cancellation);

        await DatabaseSetup.RunAsync(db, seedDemoProducts: true, Cancellation);

        Assert.Equal("REAL-1", (await db.Products.AsNoTracking().SingleAsync(Cancellation)).Sku);
    }
}
