using Microsoft.EntityFrameworkCore;

namespace Carts.IntegrationTests;

public class MigrationTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_migrations_build_the_schema_on_an_empty_database_and_can_run_again()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);

        await db.Database.MigrateAsync(Cancellation);
        await db.Database.MigrateAsync(Cancellation);

        Assert.Equal(
            ["__EFMigrationsHistory", "cart_items", "carts", "products"],
            await SchemaReader.TableNamesAsync(connectionString, Cancellation));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Cancellation));
    }

    [Fact]
    public void The_model_has_no_change_that_is_missing_from_the_migrations()
    {
        using var db = postgres.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task The_columns_are_the_ones_the_design_calls_for()
    {
        Assert.Equal(
            [
                "cart_items.sku character varying(64) not null",
                "cart_items.cart_id uuid not null",
                "cart_items.product_name character varying(200) not null",
                "cart_items.unit_price numeric(19,4) not null",
                "cart_items.quantity integer not null",
                "cart_items.added_at timestamp with time zone not null",
                "carts.id uuid not null",
                "carts.user_id character varying(255) not null",
                "carts.currency character(3) null",
                "carts.version integer not null",
                "carts.created_at timestamp with time zone not null",
                "carts.updated_at timestamp with time zone not null",
                "products.sku character varying(64) not null",
                "products.name character varying(200) not null",
                "products.unit_price numeric(19,4) not null",
                "products.currency character(3) not null",
                "products.is_active boolean not null",
            ],
            await SchemaReader.ColumnsAsync(postgres.ConnectionString, Cancellation));
    }

    [Fact]
    public async Task The_keys_checks_and_unique_index_are_the_ones_the_design_calls_for()
    {
        Assert.Equal(
            [
                "ck_cart_items_quantity: CHECK (((quantity >= 1) AND (quantity <= 99)))",
                "ck_cart_items_unit_price: CHECK ((unit_price >= (0)::numeric))",
                "ck_carts_currency: CHECK ((currency ~ '^[A-Z]{3}$'::text))",
                "ck_carts_user_id: CHECK (((user_id)::text <> ''::text))",
                "ck_carts_version: CHECK ((version >= 0))",
                "ck_products_currency: CHECK ((currency ~ '^[A-Z]{3}$'::text))",
                "ck_products_name: CHECK (((name)::text ~ '\\S'::text))",
                "ck_products_sku: CHECK (((sku)::text ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'::text))",
                "ck_products_unit_price: CHECK ((unit_price >= (0)::numeric))",
                "fk_cart_items_carts: FOREIGN KEY (cart_id) REFERENCES carts(id) ON DELETE CASCADE",
                "pk_cart_items: PRIMARY KEY (cart_id, sku)",
                "pk_carts: PRIMARY KEY (id)",
                "pk_products: PRIMARY KEY (sku)",
                "ux_carts_user_id: btree (user_id)",
            ],
            await SchemaReader.ConstraintsAndIndexesAsync(postgres.ConnectionString, Cancellation));
    }
}
