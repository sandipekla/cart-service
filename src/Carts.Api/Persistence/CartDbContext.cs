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

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly);
}
