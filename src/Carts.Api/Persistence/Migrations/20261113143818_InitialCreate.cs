using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carts.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                    table.CheckConstraint("ck_carts_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_carts_user_id", "user_id <> ''");
                    table.CheckConstraint("ck_carts_version", "version >= 0");
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.sku);
                    table.CheckConstraint("ck_products_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_products_name", "name ~ '\\S'");
                    table.CheckConstraint("ck_products_sku", "sku ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_products_unit_price", "unit_price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                columns: table => new
                {
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_items", x => new { x.cart_id, x.sku });
                    table.CheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 99");
                    table.CheckConstraint("ck_cart_items_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_cart_items_carts",
                        column: x => x.cart_id,
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_carts_user_id",
                table: "carts",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_items");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "carts");
        }
    }
}
