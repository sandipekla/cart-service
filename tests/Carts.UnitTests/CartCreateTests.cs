using Carts.Domain;

namespace Carts.UnitTests;

public class CartCreateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_cart_is_empty_at_version_zero()
    {
        var cart = Cart.Create("alice", Now);

        Assert.Equal(7, cart.Id.Version);
        Assert.Equal("alice", cart.UserId);
        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(0, cart.Version);
        Assert.Equal(0m, cart.Total);
        Assert.Equal(Now, cart.CreatedAt);
        Assert.Equal(Now, cart.UpdatedAt);
    }

    [Fact]
    public void A_new_cart_stores_its_times_in_utc()
    {
        var zagrebMorning = new DateTimeOffset(2026, 11, 13, 11, 0, 0, TimeSpan.FromHours(1));

        var cart = Cart.Create("alice", zagrebMorning);

        Assert.Equal(TimeSpan.Zero, cart.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, cart.UpdatedAt.Offset);
        Assert.Equal(Now, cart.CreatedAt);
    }

    [Fact]
    public void The_lines_cannot_be_changed_from_outside_the_cart()
    {
        var cart = Cart.Create("alice", Now);
        cart.SetItem(TestProducts.TShirt, 2, Now);

        var lines = Assert.IsAssignableFrom<ICollection<CartItem>>(cart.Items);

        Assert.True(lines.IsReadOnly);
        Assert.Throws<NotSupportedException>(lines.Clear);
    }

    [Fact]
    public void Two_new_carts_get_different_ids()
    {
        Assert.NotEqual(Cart.Create("alice", Now).Id, Cart.Create("alice", Now).Id);
    }

    [Fact]
    public void A_user_id_may_be_as_long_as_the_limit_but_no_longer()
    {
        var atTheLimit = new string('u', 255);

        Assert.Equal(atTheLimit, Cart.Create(atTheLimit, Now).UserId);
        Assert.Throws<ArgumentException>(() => Cart.Create(atTheLimit + "u", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_cart_cannot_be_created_without_a_user(string? userId)
    {
        Assert.ThrowsAny<ArgumentException>(() => Cart.Create(userId!, Now));
    }
}
