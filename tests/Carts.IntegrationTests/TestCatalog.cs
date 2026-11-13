using Carts.Api.Products;

namespace Carts.IntegrationTests;

/// <summary>Puts products into the test database. Every SKU is new, so tests never share a product.</summary>
internal static class TestCatalog
{
    public static async Task<string> AddAsync(
        PostgresFixture postgres,
        string name = "Notebook A5",
        decimal unitPrice = 3.49m,
        string currency = "EUR",
        bool isActive = true)
    {
        var sku = $"P-{Guid.NewGuid():N}".ToUpperInvariant();
        await using var db = postgres.CreateContext();
        db.Products.Add(new Product(sku, name, unitPrice, currency, isActive));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return sku;
    }
}
