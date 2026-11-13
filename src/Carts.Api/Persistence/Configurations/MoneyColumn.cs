using Carts.Domain;

namespace Carts.Api.Persistence.Configurations;

/// <summary>numeric(19,4): room for every price the cart's limit allows.</summary>
internal static class MoneyColumn
{
    /// <summary>CartLimits.MaxUnitPrice has fifteen digits before the decimal point.</summary>
    private const int IntegerDigits = 15;

    public const int Scale = CartLimits.MaxPriceDecimals;
    public const int Precision = IntegerDigits + Scale;
}
