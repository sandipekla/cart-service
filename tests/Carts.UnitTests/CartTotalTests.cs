using Carts.Domain;

namespace Carts.UnitTests;

public class CartTotalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_line_total_is_exact_decimal_arithmetic()
    {
        var cart = Cart.Create("alice", Now);

        cart.SetItem(TestProducts.TShirt, 3, Now);

        Assert.Equal(59.97m, Assert.Single(cart.Items).LineTotal);
    }

    [Fact]
    public void The_total_is_the_sum_of_the_line_totals()
    {
        var cart = Cart.Create("alice", Now);

        cart.SetItem(TestProducts.TShirt, 2, Now);
        cart.SetItem(TestProducts.Mug, 3, Now);

        Assert.Equal(56.48m, cart.Total);
    }

    [Fact]
    public void The_largest_cart_the_rules_allow_still_has_a_total()
    {
        var cart = Cart.Create("alice", Now);
        for (var number = 1; number <= 50; number++)
        {
            cart.SetItem(new($"SKU-{number:000}", "Yacht", 999_999_999_999_999.9999m, "EUR", isActive: true), 99, Now);
        }

        Assert.Equal(4_949_999_999_999_999_999.505m, cart.Total);
    }

    [Fact]
    public void A_price_with_three_decimals_keeps_them_in_the_total()
    {
        var cart = Cart.Create("alice", Now);

        cart.SetItem(new("DATES-1KG", "Dates 1 kg", 1.375m, "KWD", isActive: true), 3, Now);

        Assert.Equal(4.125m, cart.Total);
    }
}
