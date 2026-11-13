namespace Carts.Domain;

/// <summary>A user's cart. Every rule about what a cart may contain lives here.</summary>
public sealed class Cart
{
    private readonly List<CartItem> _items = [];

    private Cart(Guid id, string userId, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        CreatedAt = CartTime.Normalize(createdAt);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public string UserId { get; private set; }

    /// <summary>Currency code shared by every line; null while the cart has no lines.</summary>
    public string? Currency { get; private set; }

    /// <summary>Incremented once for every change that actually altered the cart.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    public decimal Total => _items.Sum(item => item.LineTotal);

    public static Cart Create(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (userId.Length > CartLimits.MaxUserIdLength)
        {
            throw new ArgumentException($"A user id is at most {CartLimits.MaxUserIdLength} characters.", nameof(userId));
        }

        return new Cart(Guid.CreateVersion7(now), userId, now);
    }

    /// <summary>
    /// Sets the quantity of a product's line to an absolute value, adding the line if the cart
    /// does not have it yet, and refreshes the line's name and price from the product.
    /// Setting the same values again changes nothing.
    /// </summary>
    /// <returns>Null when the cart accepted the command; otherwise the rule that refused it.</returns>
    public CartError? SetItem(ProductSnapshot product, int quantity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (quantity is < 1 or > CartLimits.MaxQuantity)
        {
            return CartError.InvalidQuantity;
        }

        if (!product.IsActive)
        {
            return CartError.ProductUnavailable;
        }

        if (Currency is not null && Currency != product.Currency)
        {
            return CartError.CurrencyMismatch;
        }

        var line = FindItem(product.Sku);
        if (line is null)
        {
            if (_items.Count >= CartLimits.MaxItems)
            {
                return CartError.ItemLimitExceeded;
            }

            _items.Add(new CartItem(product.Sku, product.Name, product.UnitPrice, quantity, CartTime.Normalize(now)));
            Currency = product.Currency;
            MarkChanged(now);
        }
        else if (line.Update(product.Name, product.UnitPrice, quantity))
        {
            MarkChanged(now);
        }

        return null;
    }

    /// <summary>Removes a product's line. Removing a line that is not there changes nothing.</summary>
    public void RemoveItem(string sku, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sku);

        var line = FindItem(sku);
        if (line is null)
        {
            return;
        }

        _items.Remove(line);
        if (_items.Count == 0)
        {
            Currency = null;
        }

        MarkChanged(now);
    }

    /// <summary>Removes every line as one change. The cart itself, and its id, stay.</summary>
    public void Clear(DateTimeOffset now)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Currency = null;
        MarkChanged(now);
    }

    // SKUs are compared exactly, including case.
    private CartItem? FindItem(string sku) => _items.Find(item => item.Sku == sku);

    private void MarkChanged(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = CartTime.Normalize(now);
    }
}
