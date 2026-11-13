using System.Globalization;
using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carts.Api.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            // Product rows arrive from outside the cart's own code, so the table itself refuses
            // what a cart could not use. The SKU pattern is the SQL form of SkuFormat.IsValid.
            table.HasCheckConstraint(
                "ck_products_sku",
                string.Create(CultureInfo.InvariantCulture, $"sku ~ '^[A-Za-z0-9][A-Za-z0-9._-]{{0,{CartLimits.MaxSkuLength - 1}}}$'"));
            table.HasCheckConstraint("ck_products_name", @"name ~ '\S'");
            table.HasCheckConstraint("ck_products_unit_price", "unit_price >= 0");
            table.HasCheckConstraint("ck_products_currency", CurrencyColumn.ShapeCheck);
        });

        builder.HasKey(product => product.Sku).HasName("pk_products");

        builder.Property(product => product.Sku).HasColumnName("sku").HasMaxLength(CartLimits.MaxSkuLength);
        builder.Property(product => product.Name).HasColumnName("name").HasMaxLength(CartLimits.MaxProductNameLength);
        builder.Property(product => product.UnitPrice).HasColumnName("unit_price").HasPrecision(MoneyColumn.Precision, MoneyColumn.Scale);
        builder.Property(product => product.Currency).HasColumnName("currency").HasMaxLength(CurrencyColumn.Length).IsFixedLength();
        builder.Property(product => product.IsActive).HasColumnName("is_active");
    }
}
