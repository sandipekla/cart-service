using System.Net;
using System.Text.Json;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Carts.IntegrationTests;

public class CartReadTests(PostgresFixture postgres)
{
    private const string CartPath = "/api/v1/cart";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_who_never_had_a_cart_gets_the_empty_cart_and_nothing_is_stored()
    {
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var response = await client.GetAsync(CartPath, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"id":null,"version":0,"currency":null,"items":[],"total":0,"updatedAt":null}""",
            await response.Content.ReadAsStringAsync(Cancellation));
        await using var db = postgres.CreateContext();
        Assert.False(await db.Carts.AnyAsync(cart => cart.UserId == userId, Cancellation));
    }

    [Fact]
    public async Task A_stored_cart_is_returned_with_its_lines_and_totals()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now.AddMinutes(1));
        cart.SetItem(TestData.Mug, 3, TestData.Now.AddMinutes(2));
        await SaveAsync(cart);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        using var body = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));

        var root = body.RootElement;
        Assert.Equal(cart.Id, root.GetProperty("id").GetGuid());
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("EUR", root.GetProperty("currency").GetString());
        Assert.Equal(62.48m, root.GetProperty("total").GetDecimal());
        Assert.Equal(TestData.Now.AddMinutes(2), root.GetProperty("updatedAt").GetDateTimeOffset());
        var first = root.GetProperty("items")[0];
        Assert.Equal("TSHIRT-BLK-M", first.GetProperty("sku").GetString());
        Assert.Equal("T-shirt black M", first.GetProperty("name").GetString());
        Assert.Equal(19.99m, first.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2, first.GetProperty("quantity").GetInt32());
        Assert.Equal(39.98m, first.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(2, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Amounts_are_written_without_padding_zeros()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 1, TestData.Now);
        cart.SetItem(new ProductSnapshot("ROUND-PRICE", "Round price", 20.0000m, "EUR", isActive: true), 1, TestData.Now.AddMinutes(1));
        await SaveAsync(cart);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var json = await client.GetStringAsync(CartPath, Cancellation);

        Assert.Contains("\"unitPrice\":19.99,", json, StringComparison.Ordinal);
        Assert.Contains("\"unitPrice\":20,", json, StringComparison.Ordinal);
        Assert.Contains("\"total\":39.99,", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lines_come_back_in_the_order_they_were_added()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.Saffron, 1, TestData.Now.AddMinutes(1));
        cart.SetItem(TestData.TShirt, 1, TestData.Now.AddMinutes(2));
        cart.SetItem(TestData.Mug, 1, TestData.Now.AddMinutes(3));
        await SaveAsync(cart);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        using var body = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));

        Assert.Equal(
            ["SAFFRON-1G", "TSHIRT-BLK-M", "MUG-WHT"],
            body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sku").GetString()));
    }

    [Fact]
    public async Task Lines_added_at_the_same_moment_come_back_in_sku_order()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 1, TestData.Now);
        cart.SetItem(TestData.Saffron, 1, TestData.Now);
        cart.SetItem(TestData.Mug, 1, TestData.Now);
        await SaveAsync(cart);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        using var body = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));

        Assert.Equal(
            ["MUG-WHT", "SAFFRON-1G", "TSHIRT-BLK-M"],
            body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sku").GetString()));
    }

    [Fact]
    public async Task One_user_never_sees_another_users_cart()
    {
        var owner = TestData.NewUserId();
        var cart = Cart.Create(owner, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveAsync(cart);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var someoneElse = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        using var body = JsonDocument.Parse(await someoneElse.GetStringAsync(CartPath, Cancellation));

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_cart_answer_is_never_stored_by_a_cache()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var response = await client.GetAsync(CartPath, Cancellation);

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private async Task SaveAsync(Cart cart)
    {
        await using var db = postgres.CreateContext();
        db.Carts.Add(cart);
        await db.SaveChangesAsync(Cancellation);
    }
}
