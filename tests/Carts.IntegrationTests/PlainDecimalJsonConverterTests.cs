using System.Text.Json;
using Carts.Api.Json;

namespace Carts.IntegrationTests;

public class PlainDecimalJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new PlainDecimalJsonConverter() } };

    public static TheoryData<decimal, string> Amounts => new()
    {
        { 19.9900m, "19.99" },
        { 20.0000m, "20" },
        { 0m, "0" },
        { 0.0000m, "0" },
        { -0.0m, "0" },
        { 0.5m, "0.5" },
        { 0.0001m, "0.0001" },
        { 0.00001m, "0.00001" },
        { 0.0000000000000000000000000001m, "0.0000000000000000000000000001" },
        { 999_999_999_999_999.9999m, "999999999999999.9999" },
        { 4_949_999_999_999_999_999.505m, "4949999999999999999.505" },
        { -12.3400m, "-12.34" },
        { decimal.MaxValue, "79228162514264337593543950335" },
    };

    [Theory]
    [MemberData(nameof(Amounts))]
    public void An_amount_is_written_in_full_without_padding_and_without_an_exponent(decimal amount, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(amount, Options));
    }

    [Theory]
    [MemberData(nameof(Amounts))]
    public void What_was_written_reads_back_as_the_same_amount(decimal amount, string written)
    {
        Assert.Equal(amount, JsonSerializer.Deserialize<decimal>(written, Options));
    }
}
