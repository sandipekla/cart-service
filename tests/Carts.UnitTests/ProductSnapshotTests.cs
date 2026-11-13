using Carts.Domain;

namespace Carts.UnitTests;

public class ProductSnapshotTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_product_needs_a_sku(string? sku)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProductSnapshot(sku!, "Mug white", 5.50m, "EUR", isActive: true));
    }

    [Theory]
    [InlineData(" MUG-WHT")]
    [InlineData("MUG WHT")]
    [InlineData("MUG-WHT\n")]
    [InlineData("..")]
    public void A_product_needs_a_well_formed_sku(string sku)
    {
        Assert.Throws<ArgumentException>(() => new ProductSnapshot(sku, "Mug white", 5.50m, "EUR", isActive: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_product_needs_a_name(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProductSnapshot("MUG-WHT", name!, 5.50m, "EUR", isActive: true));
    }

    [Fact]
    public void A_sku_may_be_as_long_as_the_limit_but_no_longer()
    {
        var atTheLimit = new string('S', 64);

        Assert.Equal(atTheLimit, new ProductSnapshot(atTheLimit, "Mug white", 5.50m, "EUR", isActive: true).Sku);
        Assert.Throws<ArgumentException>(() => new ProductSnapshot(atTheLimit + "S", "Mug white", 5.50m, "EUR", isActive: true));
    }

    [Fact]
    public void A_name_may_be_as_long_as_the_limit_but_no_longer()
    {
        var atTheLimit = new string('n', 200);

        Assert.Equal(atTheLimit, new ProductSnapshot("MUG-WHT", atTheLimit, 5.50m, "EUR", isActive: true).Name);
        Assert.Throws<ArgumentException>(() => new ProductSnapshot("MUG-WHT", atTheLimit + "n", 5.50m, "EUR", isActive: true));
    }

    [Fact]
    public void A_price_may_be_as_high_as_the_limit_but_no_higher()
    {
        var product = new ProductSnapshot("YACHT", "Yacht", 999_999_999_999_999.9999m, "EUR", isActive: true);

        Assert.Equal(999_999_999_999_999.9999m, product.UnitPrice);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductSnapshot("YACHT", "Yacht", 1_000_000_000_000_000m, "EUR", isActive: true));
    }

    [Fact]
    public void A_price_has_at_most_four_decimal_places()
    {
        Assert.Throws<ArgumentException>(() => new ProductSnapshot("MUG-WHT", "Mug white", 1.23456m, "EUR", isActive: true));
    }

    [Fact]
    public void Trailing_zeros_do_not_count_as_decimal_places()
    {
        var product = new ProductSnapshot("MUG-WHT", "Mug white", 1.2500000m, "EUR", isActive: true);

        Assert.Equal(1.25m, product.UnitPrice);
    }

    [Fact]
    public void Negative_zero_is_zero_and_therefore_allowed()
    {
        var product = new ProductSnapshot("STICKER", "Sticker", -0.0m, "EUR", isActive: true);

        Assert.Equal(0m, product.UnitPrice);
    }

    [Fact]
    public void A_price_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductSnapshot("MUG-WHT", "Mug white", -0.01m, "EUR", isActive: true));
    }

    [Fact]
    public void A_free_product_is_allowed()
    {
        var product = new ProductSnapshot("STICKER", "Sticker", 0m, "EUR", isActive: true);

        Assert.Equal(0m, product.UnitPrice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("EU")]
    [InlineData("E1R")]
    [InlineData("ÉUR")]
    public void A_currency_is_three_uppercase_ascii_letters(string? currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProductSnapshot("MUG-WHT", "Mug white", 5.50m, currency!, isActive: true));
    }

    [Fact]
    public void Two_snapshots_with_the_same_values_are_equal()
    {
        Assert.Equal(TestProducts.TShirtWith(), TestProducts.TShirtWith());
    }
}
