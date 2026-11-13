using Carts.Api.Persistence;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Products;

/// <summary>Looks products up in the service's own table. It never calls another service.</summary>
public sealed class ProductCatalog(CartDbContext db)
{
    public async Task<ProductSnapshot?> FindAsync(string sku, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Sku == sku, cancellationToken);

        return product?.ToSnapshot();
    }
}
