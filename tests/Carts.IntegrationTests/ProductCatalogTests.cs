using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Carts.IntegrationTests;

public class ProductCatalogTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stored_product_is_found_with_all_its_values()
    {
        var sku = await AddProductAsync(name: "Notebook A5", unitPrice: 3.4900m, currency: "EUR", isActive: true);
        await using var db = postgres.CreateContext();

        var product = await new ProductCatalog(db, NullLogger<ProductCatalog>.Instance).FindAsync(sku, Cancellation);

        Assert.NotNull(product);
        Assert.Equal(sku, product.Sku);
        Assert.Equal("Notebook A5", product.Name);
        Assert.Equal(3.49m, product.UnitPrice);
        Assert.Equal("EUR", product.Currency);
        Assert.True(product.IsActive);
    }

    [Fact]
    public async Task An_inactive_product_is_found_and_says_so()
    {
        var sku = await AddProductAsync(name: "Poster 2019", unitPrice: 12m, currency: "EUR", isActive: false);
        await using var db = postgres.CreateContext();

        var product = await new ProductCatalog(db, NullLogger<ProductCatalog>.Instance).FindAsync(sku, Cancellation);

        Assert.False(product!.IsActive);
    }

    [Fact]
    public async Task Skus_are_matched_exactly_including_case()
    {
        var sku = await AddProductAsync(name: "Notebook A5", unitPrice: 3.49m, currency: "EUR", isActive: true);
        await using var db = postgres.CreateContext();

        Assert.Null(await new ProductCatalog(db, NullLogger<ProductCatalog>.Instance).FindAsync(sku.ToLowerInvariant(), Cancellation));
    }

    [Theory]
    [InlineData("", "Notebook A5")]
    [InlineData("HAS SPACE", "Notebook A5")]
    [InlineData("-STARTS-WITH-HYPHEN", "Notebook A5")]
    [InlineData("VALID-SKU-BUT-NO-NAME", "")]
    [InlineData("VALID-SKU-BLANK-NAME", "   ")]
    [InlineData("VALID-SKU-TAB-NAME", "\t\n")]
    public async Task The_products_table_refuses_data_the_cart_could_not_use(string sku, string name)
    {
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO products (sku, name, unit_price, currency, is_active) VALUES ({sku}, {name}, 1, 'EUR', true)",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Every_sku_the_products_table_accepts_is_one_the_cart_accepts()
    {
        string[] samples = ["A", "0", "a.b_c-1", new string('S', 64), "TSHIRT-BLK-M"];
        await using var db = postgres.CreateContext();

        foreach (var sample in samples)
        {
            var sku = sample.Length == 64 ? sample : $"{sample}{Guid.NewGuid():N}"[..Math.Min(64, sample.Length + 32)];
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO products (sku, name, unit_price, currency, is_active) VALUES ({sku}, 'Sample', 1, 'EUR', true) ON CONFLICT DO NOTHING",
                Cancellation);

            Assert.True(SkuFormat.IsValid(sku));
            Assert.NotNull(await new ProductCatalog(db, NullLogger<ProductCatalog>.Instance).FindAsync(sku, Cancellation));
        }
    }

    private async Task<string> AddProductAsync(string name, decimal unitPrice, string currency, bool isActive)
    {
        var sku = $"P-{Guid.NewGuid():N}".ToUpperInvariant();
        await using var db = postgres.CreateContext();
        db.Products.Add(new Product(sku, name, unitPrice, currency, isActive));
        await db.SaveChangesAsync(Cancellation);
        return sku;
    }
}
