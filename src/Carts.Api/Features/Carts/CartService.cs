using Carts.Api.Persistence;
using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Carts.Api.Features.Carts;

/// <summary>What the API can do with a cart. Endpoints translate HTTP; this class does the work.</summary>
public sealed partial class CartService(
    CartDbContext db,
    ProductCatalog products,
    TimeProvider time,
    IOptions<CartOptions> options,
    ILogger<CartService> logger)
{
    /// <summary>
    /// The user's cart, or null when the user has none. Reading never creates one and never
    /// waits for a change in progress: it is one statement, so it sees the cart either before
    /// or after that change, never half of it.
    /// </summary>
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
        // Looked up before the cart lock is taken: a product is not the user's data, and a
        // request for one that does not exist should make nobody wait.
        var product = await products.FindAsync(sku, cancellationToken);
        if (product is null)
        {
            return CartOutcome.Refused(CartFailure.ProductNotFound);
        }

        return await ChangeAsync(userId, createIfMissing: true, (cart, now) => cart.SetItem(product, quantity, now), cancellationToken);
    }

    /// <summary>Removes a product from the user's cart. Nothing to remove is not an error.</summary>
    public Task<CartOutcome> RemoveItemAsync(string userId, string sku, CancellationToken cancellationToken) =>
        ChangeAsync(
            userId,
            createIfMissing: false,
            (cart, now) =>
            {
                cart.RemoveItem(sku, now);
                return null;
            },
            cancellationToken);

    /// <summary>Empties the user's cart. The cart itself stays; a user without one still has none.</summary>
    public Task<CartOutcome> ClearAsync(string userId, CancellationToken cancellationToken) =>
        ChangeAsync(
            userId,
            createIfMissing: false,
            (cart, now) =>
            {
                cart.Clear(now);
                return null;
            },
            cancellationToken);

    /// <summary>
    /// Every change to a cart goes through here: read the cart, apply the change, store the
    /// result, all while holding the user's cart lock. Two changes to one cart therefore run
    /// one after the other, and the second one starts from what the first one stored.
    /// </summary>
    private async Task<CartOutcome> ChangeAsync(
        string userId,
        bool createIfMissing,
        Func<Cart, DateTimeOffset, CartError?> change,
        CancellationToken cancellationToken)
    {
        var lockTimeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);

        try
        {
            // Ending the transaction, by commit or by leaving this block, releases the lock.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.LockCartAsync(userId, lockTimeout, cancellationToken);

            var cart = await db.FindCartAsync(userId, cancellationToken);
            if (cart is null && !createIfMissing)
            {
                return CartOutcome.Of(null);
            }

            // The time is read under the lock, so on one clock a later change never carries an earlier time.
            var now = time.GetUtcNow();
            var isNew = cart is null;
            cart ??= Cart.Create(userId, now);
            var versionBefore = cart.Version;

            if (change(cart, now) is { } refusal)
            {
                return CartOutcome.Refused(ToFailure(refusal));
            }

            // The cart counts its own real changes; a command that changed nothing writes nothing.
            if (cart.Version != versionBefore)
            {
                if (isNew)
                {
                    db.Carts.Add(cart);
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogCartChanged(logger, cart.Id, cart.Version);
            }

            return CartOutcome.Of(cart);
        }
        catch (Exception exception) when (IsLockTimeout(exception))
        {
            LogCartBusy(logger, options.Value.LockTimeoutMs);
            return CartOutcome.Refused(CartFailure.CartBusy);
        }
        catch (DbUpdateException exception) when (IsWriteAroundTheLock(exception))
        {
            LogWriteAroundTheLock(logger, exception);
            return CartOutcome.Refused(CartFailure.ConcurrencyConflict);
        }
    }

    /// <summary>
    /// The lock timeout of the transaction ran out. It covers every wait of the change: for
    /// the cart lock, and for a row or a table something else holds. The database layer
    /// wraps the error when it comes from a query or a save, so the cause is searched for.
    /// </summary>
    private static bool IsLockTimeout(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The cart's version or one of its unique keys caught a second writer. Under the lock
    /// that cannot happen, so something changed the cart without taking it.
    /// </summary>
    private static bool IsWriteAroundTheLock(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "A change gave up after waiting {LockTimeoutMs} ms for a lock on the cart.")]
    private static partial void LogCartBusy(ILogger logger, int lockTimeoutMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "A cart was changed without the cart lock; this change was refused. Some write path does not take the lock.")]
    private static partial void LogWriteAroundTheLock(ILogger logger, Exception exception);
}
