using Carts.Api.Persistence;
using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Features.Carts;

/// <summary>What the API can do with a cart. Endpoints translate HTTP; this class does the work.</summary>
public sealed partial class CartService(
    CartDbContext db,
    ProductCatalog products,
    TimeProvider time,
    ILogger<CartService> logger)
{
    /// <summary>The user's cart, or null when the user has none. Reading never creates one.</summary>
    public Task<Cart?> FindAsync(string userId, CancellationToken cancellationToken) =>
        db.Carts
            .AsNoTracking()
            .Include(cart => cart.Items)
            .SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    /// <summary>
    /// Sets the quantity of a product in the user's cart to an absolute value. The first item
    /// a user sets creates their cart.
    /// </summary>
    public async Task<CartOutcome> SetItemAsync(string userId, string sku, int quantity, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(sku, cancellationToken);
        if (product is null)
        {
            return CartOutcome.Refused(CartFailure.ProductNotFound);
        }

        var cart = await db.FindCartAsync(userId, cancellationToken);

        // The time of a change is read only once the cart is in hand, so a later change can
        // never carry an earlier time than the one before it.
        var now = time.GetUtcNow();
        var isNew = cart is null;
        cart ??= Cart.Create(userId, now);
        var versionBefore = cart.Version;

        if (cart.SetItem(product, quantity, now) is { } refusal)
        {
            return CartOutcome.Refused(ToFailure(refusal));
        }

        if (isNew)
        {
            db.Carts.Add(cart);
        }

        return await SaveIfChangedAsync(cart, versionBefore, cancellationToken);
    }

    /// <summary>Removes a product from the user's cart. Nothing to remove is not an error.</summary>
    public async Task<CartOutcome> RemoveItemAsync(string userId, string sku, CancellationToken cancellationToken)
    {
        var cart = await db.FindCartAsync(userId, cancellationToken);
        if (cart is null)
        {
            return CartOutcome.Of(null);
        }

        var versionBefore = cart.Version;
        cart.RemoveItem(sku, time.GetUtcNow());

        return await SaveIfChangedAsync(cart, versionBefore, cancellationToken);
    }

    /// <summary>Empties the user's cart. The cart itself stays; a user without one still has none.</summary>
    public async Task<CartOutcome> ClearAsync(string userId, CancellationToken cancellationToken)
    {
        var cart = await db.FindCartAsync(userId, cancellationToken);
        if (cart is null)
        {
            return CartOutcome.Of(null);
        }

        var versionBefore = cart.Version;
        cart.Clear(time.GetUtcNow());

        return await SaveIfChangedAsync(cart, versionBefore, cancellationToken);
    }

    /// <summary>The cart counts its own real changes; a command that changed nothing writes nothing.</summary>
    private async Task<CartOutcome> SaveIfChangedAsync(Cart cart, int versionBefore, CancellationToken cancellationToken)
    {
        if (cart.Version != versionBefore)
        {
            await db.SaveChangesAsync(cancellationToken);
            LogCartChanged(logger, cart.Id, cart.Version);
        }

        return CartOutcome.Of(cart);
    }

    private static CartFailure ToFailure(CartError error) => error switch
    {
        CartError.ProductUnavailable => CartFailure.ProductUnavailable,
        CartError.CurrencyMismatch => CartFailure.CurrencyMismatch,
        CartError.ItemLimitExceeded => CartFailure.ItemLimitExceeded,
        // The quantity is validated before the service is called, so the cart refusing it
        // here, like any reason added later and not mapped, is a bug and not an answer.
        _ => throw new InvalidOperationException($"The cart refused a change for a reason the service has no answer for: {error}."),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Cart {CartId} changed to version {Version}.")]
    private static partial void LogCartChanged(ILogger logger, Guid cartId, int version);
}
