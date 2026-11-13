using Carts.Domain;

namespace Carts.Api.Products;

/// <summary>One row of this service's own copy of product data.</summary>
public sealed class Product(string sku, string name, decimal unitPrice, string currency, bool isActive)
{
    public string Sku { get; private set; } = sku;

    public string Name { get; private set; } = name;

    public decimal UnitPrice { get; private set; } = unitPrice;

    public string Currency { get; private set; } = currency;

    public bool IsActive { get; private set; } = isActive;

    public ProductSnapshot ToSnapshot() => new(Sku, Name, UnitPrice, Currency, IsActive);
}
