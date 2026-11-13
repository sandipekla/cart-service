using Carts.Domain;

namespace Carts.IntegrationTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    public static readonly ProductSnapshot TShirt = new("TSHIRT-BLK-M", "T-shirt black M", 19.99m, "EUR", isActive: true);
    public static readonly ProductSnapshot Mug = new("MUG-WHT", "Mug white", 7.50m, "EUR", isActive: true);
    public static readonly ProductSnapshot Saffron = new("SAFFRON-1G", "Saffron 1 g", 4.125m, "EUR", isActive: true);

    /// <summary>A user id no other test uses, so tests never see each other's carts.</summary>
    public static string NewUserId() => $"user-{Guid.NewGuid():N}";
}
