using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carts.Api.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts", table =>
        {
            table.HasCheckConstraint("ck_carts_user_id", "user_id <> ''");
            table.HasCheckConstraint("ck_carts_currency", CurrencyColumn.ShapeCheck);
            table.HasCheckConstraint("ck_carts_version", "version >= 0");
        });

        builder.HasKey(cart => cart.Id).HasName("pk_carts");

        // The application generates the id (UUID v7), so the database must not.
        builder.Property(cart => cart.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(cart => cart.UserId).HasColumnName("user_id").HasMaxLength(CartLimits.MaxUserIdLength);
        builder.HasIndex(cart => cart.UserId).IsUnique().HasDatabaseName("ux_carts_user_id");

        builder.Property(cart => cart.Currency).HasColumnName("currency").HasMaxLength(CurrencyColumn.Length).IsFixedLength();

        // A save whose cart row no longer has the version it read updates no row and fails.
        // It refuses a stale write; it does not make two concurrent writes both succeed.
        builder.Property(cart => cart.Version).HasColumnName("version").IsConcurrencyToken();

        builder.Property(cart => cart.CreatedAt).HasColumnName("created_at");
        builder.Property(cart => cart.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(cart => cart.Items)
            .WithOne()
            .HasForeignKey(CartItemConfiguration.CartId)
            .HasConstraintName("fk_cart_items_carts")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
