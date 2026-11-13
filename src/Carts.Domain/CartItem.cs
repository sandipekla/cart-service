namespace Carts.Domain;

/// <summary>One line of a cart: a product and how many of it.</summary>
public sealed class CartItem
{
    internal CartItem(string sku, string productName, decimal unitPrice, int quantity, DateTimeOffset addedAt)
    {
        Sku = sku;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        AddedAt = addedAt;
    }

    public string Sku { get; private set; }

    /// <summary>Name as it was when the line was last set. A snapshot, not a live value.</summary>
    public string ProductName { get; private set; }

    /// <summary>Price as it was when the line was last set. A snapshot, not a live value.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>Applies the latest name, price and quantity. Returns false when nothing differs.</summary>
    internal bool Update(string productName, decimal unitPrice, int quantity)
    {
        if (ProductName == productName && UnitPrice == unitPrice && Quantity == quantity)
        {
            return false;
        }

        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        return true;
    }
}
