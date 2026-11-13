using Carts.Api.Persistence;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.Api.Products;

/// <summary>Looks products up in the service's own table. It never calls another service.</summary>
public sealed partial class ProductCatalog(CartDbContext db, ILogger<ProductCatalog> logger)
{
    public async Task<ProductSnapshot?> FindAsync(string sku, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Sku == sku, cancellationToken);

        try
        {
            return product?.ToSnapshot();
        }
        catch (ArgumentException exception)
        {
            // The row holds a value the cart's rules refuse, which the table's own checks did
            // not catch. That is bad data on our side: report it, and treat the product as
            // one that cannot be put in a cart.
            LogUnusableProduct(logger, sku, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Product {Sku} holds data a cart cannot use and is treated as not found.")]
    private static partial void LogUnusableProduct(ILogger logger, string sku, Exception exception);
}
