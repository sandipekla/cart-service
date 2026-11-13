namespace Carts.Api.Products;

/// <summary>
/// A small catalog for local use. It holds an inactive product and one in another currency,
/// so the availability and currency rules can be tried by hand.
/// </summary>
public static class DemoProducts
{
    public static IReadOnlyList<Product> Create() =>
    [
        new("TSHIRT-BLK-M", "T-shirt black M", 19.99m, "EUR", isActive: true),
        new("TSHIRT-WHT-L", "T-shirt white L", 19.99m, "EUR", isActive: true),
        new("HOODIE-GRY-M", "Hoodie grey M", 49.90m, "EUR", isActive: true),
        new("MUG-WHT", "Mug white 300 ml", 7.50m, "EUR", isActive: true),
        new("CAP-NAVY", "Cap navy", 14.00m, "EUR", isActive: true),
        new("SOCKS-3PK", "Socks, pack of 3", 9.95m, "EUR", isActive: true),
        new("POSTER-2019", "Poster 2019 edition", 12.00m, "EUR", isActive: false),
        new("GIFTCARD-USD-25", "Gift card 25 USD", 25.00m, "USD", isActive: true),
    ];
}
