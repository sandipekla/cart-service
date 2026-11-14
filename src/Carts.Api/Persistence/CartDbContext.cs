using System.Globalization;
using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Persistence;

public sealed class CartDbContext(DbContextOptions<CartDbContext> options) : DbContext(options)
{
    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<Product> Products => Set<Product>();

    /// <summary>Loads a user's cart with its lines in one statement, tracked for changes.</summary>
    public Task<Cart?> FindCartAsync(string userId, CancellationToken cancellationToken) =>
        Carts
            .Include(cart => cart.Items)
            .SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    /// <summary>
    /// Waits for the user's cart lock and holds it until the current transaction ends, so
    /// that two changes to one cart never run at the same time, on any instance of the API.
    /// It is an advisory lock on the hash of the user id: it locks no row, and it works for
    /// a user who has no cart yet. The timeout then applies to every lock the transaction
    /// waits for; a longer wait fails with PostgreSQL's "lock not available" error. Two
    /// users whose ids hash alike would briefly wait for each other, which is harmless.
    /// </summary>
    public Task LockCartAsync(string userId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // PostgreSQL reads a lock timeout of zero as no timeout at all.
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.FromMilliseconds(1));

        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The cart lock lives in a transaction; begin one before taking it.");
        }

        // SET LOCAL takes no parameters; set_config with "is_local" does the same and does.
        var lockTimeout = string.Create(CultureInfo.InvariantCulture, $"{(long)timeout.TotalMilliseconds}ms");

        return Database.ExecuteSqlAsync(
            $"""
            SELECT set_config('lock_timeout', {lockTimeout}, true);
            SELECT pg_advisory_xact_lock(hashtextextended({userId}, 0));
            """,
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly);
}
