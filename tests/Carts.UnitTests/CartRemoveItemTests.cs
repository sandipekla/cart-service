using Carts.Domain;

namespace Carts.UnitTests;

public class CartRemoveItemTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset First = Created.AddMinutes(1);
    private static readonly DateTimeOffset Second = Created.AddMinutes(2);

    [Fact]
    public void Removing_a_line_keeps_the_others_and_the_currency()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);
        cart.SetItem(TestProducts.Mug, 1, First);

        cart.RemoveItem("TSHIRT-BLK-M", Second);

        Assert.Equal("MUG-WHT", Assert.Single(cart.Items).Sku);
        Assert.Equal("EUR", cart.Currency);
        Assert.Equal(3, cart.Version);
        Assert.Equal(Second, cart.UpdatedAt);
    }

    [Fact]
    public void Removing_a_line_that_is_not_the_first_removes_exactly_that_line()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);
        cart.SetItem(TestProducts.Mug, 1, First);
        cart.SetItem(TestProducts.Numbered(1), 4, First);

        cart.RemoveItem("MUG-WHT", Second);

        Assert.Equal(2, cart.Items.Count);
        Assert.Contains(cart.Items, item => item.Sku == "TSHIRT-BLK-M" && item.Quantity == 2);
        Assert.Contains(cart.Items, item => item.Sku == "SKU-001" && item.Quantity == 4);
    }

    [Fact]
    public void A_missing_sku_is_a_programming_error()
    {
        var cart = Cart.Create("alice", Created);

        Assert.Throws<ArgumentNullException>(() => cart.RemoveItem(null!, Second));
    }

    [Fact]
    public void Removing_the_last_line_frees_the_currency()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.RemoveItem("TSHIRT-BLK-M", Second);

        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(2, cart.Version);
        Assert.Equal(Second, cart.UpdatedAt);
        Assert.Null(cart.SetItem(TestProducts.UsdCap, 1, Second));
        Assert.Equal("USD", cart.Currency);
    }

    [Fact]
    public void Removing_a_sku_that_is_not_in_the_cart_changes_nothing()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.RemoveItem("MUG-WHT", Second);

        Assert.Single(cart.Items);
        Assert.Equal(1, cart.Version);
        Assert.Equal(First, cart.UpdatedAt);
    }

    [Fact]
    public void Removing_matches_the_sku_exactly_including_case()
    {
        var cart = Cart.Create("alice", Created);
        cart.SetItem(TestProducts.TShirt, 2, First);

        cart.RemoveItem("tshirt-blk-m", Second);

        Assert.Single(cart.Items);
        Assert.Equal(1, cart.Version);
    }
}
