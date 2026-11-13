using Carts.Api.Persistence;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Features.Carts;

/// <summary>What the API can do with a cart. Endpoints translate HTTP; this class does the work.</summary>
public sealed class CartService(CartDbContext db)
{
    /// <summary>The user's cart, or null when the user has none. Reading never creates one.</summary>
    public Task<Cart?> FindAsync(string userId, CancellationToken cancellationToken) =>
        db.Carts
            .AsNoTracking()
            .Include(cart => cart.Items)
            .SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);
}
