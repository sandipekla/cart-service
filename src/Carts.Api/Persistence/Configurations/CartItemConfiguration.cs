using System.Globalization;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carts.Api.Persistence.Configurations;

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    /// <summary>The line's link to its cart exists only in the database, not on the domain type.</summary>
    internal const string CartId = "CartId";

    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items", table =>
        {
            table.HasCheckConstraint(
                "ck_cart_items_quantity",
                string.Create(CultureInfo.InvariantCulture, $"quantity BETWEEN 1 AND {CartLimits.MaxQuantity}"));
            table.HasCheckConstraint("ck_cart_items_unit_price", "unit_price >= 0");
        });

        builder.Property<Guid>(CartId).HasColumnName("cart_id");
        builder.HasKey(CartId, nameof(CartItem.Sku)).HasName("pk_cart_items");

        builder.Property(item => item.Sku).HasColumnName("sku").HasMaxLength(CartLimits.MaxSkuLength);
        builder.Property(item => item.ProductName).HasColumnName("product_name").HasMaxLength(CartLimits.MaxProductNameLength);
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(MoneyColumn.Precision, MoneyColumn.Scale);
        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.AddedAt).HasColumnName("added_at");
    }
}
