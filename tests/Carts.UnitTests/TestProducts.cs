using Carts.Domain;

namespace Carts.UnitTests;

internal static class TestProducts
{
    public static readonly ProductSnapshot TShirt = TShirtWith();
    public static readonly ProductSnapshot Mug = new("MUG-WHT", "Mug white", 5.50m, "EUR", isActive: true);
    public static readonly ProductSnapshot UsdCap = new("CAP-NAVY", "Cap navy", 12.00m, "USD", isActive: true);
    public static readonly ProductSnapshot Retired = new("POSTER-2019", "Poster 2019", 9.00m, "EUR", isActive: false);

    public static ProductSnapshot TShirtWith(
        string sku = "TSHIRT-BLK-M",
        string name = "T-shirt black M",
        decimal unitPrice = 19.99m,
        string currency = "EUR",
        bool isActive = true) =>
        new(sku, name, unitPrice, currency, isActive);

    public static ProductSnapshot Numbered(int number) =>
        new($"SKU-{number:000}", $"Product {number}", 1.00m, "EUR", isActive: true);
}
