using Carts.Domain;

namespace Carts.UnitTests;

public class SkuFormatTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("a.b_c-1")]
    public void Letters_digits_dots_underscores_and_hyphens_make_a_sku(string sku)
    {
        Assert.True(SkuFormat.IsValid(sku));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("A/B")]
    [InlineData("A\n")]
    [InlineData("Ä1")]
    [InlineData("AÄ")]
    [InlineData("A١")]
    [InlineData("A,B")]
    public void Anything_else_is_not_a_sku(string? sku)
    {
        Assert.False(SkuFormat.IsValid(sku));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("-A")]
    [InlineData("_A")]
    public void A_sku_starts_with_a_letter_or_a_digit(string sku)
    {
        Assert.False(SkuFormat.IsValid(sku));
    }

    [Fact]
    public void A_sku_may_be_as_long_as_the_limit_but_no_longer()
    {
        Assert.True(SkuFormat.IsValid(new string('S', 64)));
        Assert.False(SkuFormat.IsValid(new string('S', 65)));
    }
}
