namespace Carts.Api.Features.Carts;

/// <summary>Settings of the cart feature, read from the "Cart" configuration section.</summary>
public sealed class CartOptions
{
    public const string SectionName = "Cart";

    public const int MinLockTimeoutMs = 1;
    public const int MaxLockTimeoutMs = 60_000;

    /// <summary>
    /// How long a change waits for another change to the same user's cart to finish before
    /// it gives up with "cart busy". Changes take milliseconds, so the default is generous.
    /// </summary>
    public int LockTimeoutMs { get; set; } = 2000;
}
