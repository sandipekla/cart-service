using Carts.Domain;

namespace Carts.UnitTests;

public class CartClearTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset First = Created.AddMinutes(1);
    private static readonly DateTimeOffset Second = Created.AddMinutes(2);

    [Fact]
    public void Clearing_removes_every_line_as_one_change_and_keeps_the_cart()
    {
        var cart = Cart.Create("alice", Created);
        var id = cart.Id;
        cart.SetItem(TestProducts.TShirt, 2, First);
        cart.SetItem(TestProducts.Mug, 1, First);

        cart.Clear(Second);

        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(0m, cart.Total);
        Assert.Equal(3, cart.Version);
        Assert.Equal(Second, cart.UpdatedAt);
        Assert.Equal(id, cart.Id);
    }

    [Fact]
    public void Clearing_an_empty_cart_changes_nothing()
    {
        var cart = Cart.Create("alice", Created);

        cart.Clear(Second);

        Assert.Equal(0, cart.Version);
        Assert.Equal(Created, cart.UpdatedAt);
    }
}
