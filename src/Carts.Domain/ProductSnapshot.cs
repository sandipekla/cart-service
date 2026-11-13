namespace Carts.Domain;

/// <summary>
/// What the cart needs to know about a product at the moment a line is set. A snapshot checks
/// its own values when it is created, so a cart never has to.
/// </summary>
public sealed record ProductSnapshot
{
    public ProductSnapshot(string sku, string name, decimal unitPrice, string currency, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(currency);

        if (!SkuFormat.IsValid(sku))
        {
            throw new ArgumentException("The SKU is not well formed.", nameof(sku));
        }

        if (name.Length > CartLimits.MaxProductNameLength)
        {
            throw new ArgumentException($"A product name is at most {CartLimits.MaxProductNameLength} characters.", nameof(name));
        }

        if (unitPrice is < 0 or > CartLimits.MaxUnitPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), $"A price is between 0 and {CartLimits.MaxUnitPrice}.");
        }

        if (decimal.Round(unitPrice, CartLimits.MaxPriceDecimals) != unitPrice)
        {
            throw new ArgumentException($"A price has at most {CartLimits.MaxPriceDecimals} decimal places.", nameof(unitPrice));
        }

        // Only the shape of an ISO 4217 code is checked; which currencies exist is the catalog's knowledge.
        if (currency.Length != CartLimits.CurrencyCodeLength || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("A currency code is three uppercase letters.", nameof(currency));
        }

        Sku = sku;
        Name = name;
        UnitPrice = unitPrice;
        Currency = currency;
        IsActive = isActive;
    }

    public string Sku { get; }

    public string Name { get; }

    public decimal UnitPrice { get; }

    public string Currency { get; }

    public bool IsActive { get; }
}
