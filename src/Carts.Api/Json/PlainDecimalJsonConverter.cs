using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carts.Api.Json;

/// <summary>
/// Writes an amount without padding zeros: 19.99, never 19.9900, whatever scale the database
/// returned it with. Formatting an amount for its currency is the client's job.
/// </summary>
public sealed class PlainDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    // Every significant digit a decimal can hold (28 after the point), no padding, and never
    // an exponent, however small the value.
    private const string FullPrecision = "0.############################";

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(FullPrecision, CultureInfo.InvariantCulture));
}
