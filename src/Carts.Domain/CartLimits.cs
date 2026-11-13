namespace Carts.Domain;

/// <summary>The limits a cart enforces, defined once.</summary>
public static class CartLimits
{
    public const int MaxQuantity = 99;
    public const int MaxItems = 50;
    public const int MaxUserIdLength = 255;
    public const int MaxSkuLength = 64;
    public const int MaxProductNameLength = 200;
    public const int CurrencyCodeLength = 3;

    /// <summary>Fifteen digits before the decimal point and four after it.</summary>
    public const decimal MaxUnitPrice = 999_999_999_999_999.9999m;
    public const int MaxPriceDecimals = 4;
}
