using System.Globalization;
using Carts.Domain;

namespace Carts.Api.Persistence.Configurations;

/// <summary>char(3) holding three uppercase letters, wherever a currency code is stored.</summary>
internal static class CurrencyColumn
{
    public const int Length = CartLimits.CurrencyCodeLength;

    public static readonly string ShapeCheck =
        string.Create(CultureInfo.InvariantCulture, $"currency ~ '^[A-Z]{{{Length}}}$'");
}
