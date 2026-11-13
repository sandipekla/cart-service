using Carts.Domain;

namespace Carts.UnitTests;

public class CartSetItemTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset First = Created.AddMinutes(1);
    private static readonly DateTimeOffset Second = Created.AddMinutes(2);

    [Fact]
    public void Setting_the_first_item_adds_a_line_and_fixes_the_currency()
    {
        var cart = Cart.Create("alice", Created);

        var error = cart.SetItem(TestProducts.TShirt, 2, First);

        Assert.Null(error);
        var line = Assert.Single(cart.Items);
        Assert.Equal("TSHIRT-BLK-M", line.Sku);
        Assert.Equal("T-shirt black M", line.ProductName);
        Assert.Equal(19.99m, line.UnitPrice);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(First, line.AddedAt);
        Assert.Equal("EUR", cart.Currency);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
        Assert.Equal(Created, cart.CreatedAt);
    }

    [Fact]
    public void Setting_an_existing_item_replaces_its_quantity_instead_of_adding_to_it()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.TShirt, 5, Second);

        Assert.Null(error);
        var line = Assert.Single(cart.Items);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(First, line.AddedAt);
        Assert.Equal(2, cart.Version);
        Assert.Equal(Second, cart.UpdatedAt);
    }

    [Fact]
    public void A_new_price_alone_is_a_change_and_updates_the_line()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(unitPrice: 17.49m), 2, Second);

        Assert.Equal(17.49m, Assert.Single(cart.Items).UnitPrice);
        Assert.Equal(2, cart.Version);
        Assert.Equal(Second, cart.UpdatedAt);
    }

    [Fact]
    public void A_new_name_alone_is_a_change_and_updates_the_line()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(name: "T-shirt black M (2026)"), 2, Second);

        Assert.Equal("T-shirt black M (2026)", Assert.Single(cart.Items).ProductName);
        Assert.Equal(2, cart.Version);
    }

    [Fact]
    public void A_quantity_change_also_refreshes_the_name_and_price()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(name: "T-shirt black M (2026)", unitPrice: 17.49m), 3, Second);

        var line = Assert.Single(cart.Items);
        Assert.Equal(3, line.Quantity);
        Assert.Equal("T-shirt black M (2026)", line.ProductName);
        Assert.Equal(17.49m, line.UnitPrice);
        Assert.Equal(2, cart.Version);
    }

    [Fact]
    public void Setting_an_identical_item_changes_nothing()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.TShirt, 2, Second);

        Assert.Null(error);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Fact]
    public void The_same_price_written_with_more_decimals_is_not_a_change()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(unitPrice: 19.9900m), 2, Second);

        Assert.Equal(1, cart.Version);
    }

    [Fact]
    public void A_second_product_becomes_a_second_line()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.Mug, 1, Second);

        Assert.Equal(2, cart.Items.Count);
        Assert.Contains(cart.Items, item => item.Sku == "TSHIRT-BLK-M" && item.Quantity == 2);
        Assert.Contains(cart.Items, item => item.Sku == "MUG-WHT" && item.Quantity == 1);
        Assert.Equal(2, cart.Version);
    }

    [Fact]
    public void Skus_that_differ_only_in_case_are_different_lines()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(sku: "tshirt-blk-m"), 1, Second);

        Assert.Equal(2, cart.Items.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void Quantities_at_the_edges_of_the_range_are_accepted(int quantity)
    {
        var cart = Cart.Create("alice", Created);

        var error = cart.SetItem(TestProducts.TShirt, quantity, First);

        Assert.Null(error);
        Assert.Equal(quantity, Assert.Single(cart.Items).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Quantities_outside_the_range_are_refused_and_change_nothing(int quantity)
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.TShirt, quantity, Second);

        Assert.Equal(CartError.InvalidQuantity, error);
        Assert.Equal(2, Assert.Single(cart.Items).Quantity);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void A_quantity_outside_the_range_does_not_start_a_cart(int quantity)
    {
        var cart = Cart.Create("alice", Created);

        var error = cart.SetItem(TestProducts.TShirt, quantity, First);

        Assert.Equal(CartError.InvalidQuantity, error);
        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(0, cart.Version);
    }

    [Fact]
    public void A_name_that_differs_only_in_case_is_a_change()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.SetItem(TestProducts.TShirtWith(name: "T-SHIRT BLACK M"), 2, Second);

        Assert.Equal("T-SHIRT BLACK M", Assert.Single(cart.Items).ProductName);
        Assert.Equal(2, cart.Version);
    }

    [Fact]
    public void An_inactive_product_cannot_be_added()
    {
        var cart = Cart.Create("alice", Created);

        var error = cart.SetItem(TestProducts.Retired, 1, First);

        Assert.Equal(CartError.ProductUnavailable, error);
        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(0, cart.Version);
        Assert.Equal(Created, cart.UpdatedAt);
    }

    [Fact]
    public void A_line_whose_product_became_inactive_cannot_be_changed()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.TShirtWith(isActive: false), 3, Second);

        Assert.Equal(CartError.ProductUnavailable, error);
        Assert.Equal(2, Assert.Single(cart.Items).Quantity);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Fact]
    public void A_product_in_another_currency_is_refused()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.UsdCap, 1, Second);

        Assert.Equal(CartError.CurrencyMismatch, error);
        Assert.Equal("EUR", cart.Currency);
        Assert.Single(cart.Items);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Fact]
    public void A_line_whose_product_moved_to_another_currency_cannot_be_changed()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);
        cart.SetItem(TestProducts.Mug, 1, First);

        var error = cart.SetItem(TestProducts.TShirtWith(currency: "USD"), 3, Second);

        Assert.Equal(CartError.CurrencyMismatch, error);
        Assert.Equal(2, cart.Items.Single(item => item.Sku == "TSHIRT-BLK-M").Quantity);
        Assert.Equal(2, cart.Version);
    }

    [Fact]
    public void An_invalid_quantity_is_reported_before_any_other_refusal()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        var error = cart.SetItem(TestProducts.TShirtWith(currency: "USD", isActive: false), 0, Second);

        Assert.Equal(CartError.InvalidQuantity, error);
    }

    [Fact]
    public void Times_are_stored_in_utc_whatever_offset_they_arrive_with()
    {
        var zagrebMorning = new DateTimeOffset(2026, 11, 13, 11, 0, 0, TimeSpan.FromHours(1));
        var cart = Cart.Create("alice", zagrebMorning);

        cart.SetItem(TestProducts.TShirt, 2, zagrebMorning.AddMinutes(1));

        Assert.Equal(TimeSpan.Zero, cart.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, cart.UpdatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, Assert.Single(cart.Items).AddedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 1, 0, TimeSpan.Zero), cart.UpdatedAt);
    }

    [Fact]
    public void The_line_after_the_last_allowed_one_is_refused()
    {
        var cart = CartWithFiftyLines();

        var error = cart.SetItem(TestProducts.Numbered(51), 1, Second);

        Assert.Equal(CartError.ItemLimitExceeded, error);
        Assert.Equal(50, cart.Items.Count);
        Assert.Equal(50, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Fact]
    public void An_invalid_quantity_is_reported_even_when_the_cart_is_full()
    {
        var cart = CartWithFiftyLines();

        var error = cart.SetItem(TestProducts.Numbered(51), 0, Second);

        Assert.Equal(CartError.InvalidQuantity, error);
    }

    [Fact]
    public void A_full_cart_still_accepts_changes_to_its_existing_lines()
    {
        var cart = CartWithFiftyLines();

        var error = cart.SetItem(TestProducts.Numbered(50), 7, Second);

        Assert.Null(error);
        Assert.Equal(50, cart.Items.Count);
        Assert.Equal(7, cart.Items.Single(item => item.Sku == "SKU-050").Quantity);
        Assert.Equal(51, cart.Version);
    }

    [Fact]
    public void A_missing_product_is_a_programming_error_not_a_cart_error()
    {
        var cart = Cart.Create("alice", Created);

        Assert.Throws<ArgumentNullException>(() => cart.SetItem(null!, 1, First));
    }

    private static Cart CartWithFiftyLines()
    {
        var cart = Cart.Create("alice", Created);
        for (var number = 1; number <= 50; number++)
        {
            Assert.Null(cart.SetItem(TestProducts.Numbered(number), 1, First));
        }

        return cart;
    }
}
