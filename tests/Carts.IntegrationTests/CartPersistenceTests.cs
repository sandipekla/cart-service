using System.Data.Common;
using Carts.Api.Persistence;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Carts.IntegrationTests;

public class CartPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cart_with_its_lines_reads_back_exactly_as_it_was_saved()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now.AddMinutes(1));
        cart.SetItem(TestData.Mug, 1, TestData.Now.AddMinutes(2));
        cart.SetItem(TestData.Saffron, 3, TestData.Now.AddMinutes(3));
        await SaveNewAsync(cart);

        await using var db = postgres.CreateContext();
        var loaded = await db.FindCartAsync(userId, Cancellation);

        Assert.NotNull(loaded);
        Assert.Equal(cart.Id, loaded.Id);
        Assert.Equal(userId, loaded.UserId);
        Assert.Equal("EUR", loaded.Currency);
        Assert.Equal(3, loaded.Version);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
        Assert.Equal(TestData.Now.AddMinutes(3), loaded.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.UpdatedAt.Offset);
        Assert.Equal(3, loaded.Items.Count);
        var saffron = loaded.Items.Single(item => item.Sku == "SAFFRON-1G");
        Assert.Equal("Saffron 1 g", saffron.ProductName);
        Assert.Equal(4.125m, saffron.UnitPrice);
        Assert.Equal(3, saffron.Quantity);
        Assert.Equal(TestData.Now.AddMinutes(3), saffron.AddedAt);
        Assert.Equal(59.855m, loaded.Total);
    }

    [Fact]
    public async Task Times_read_back_equal_to_what_the_cart_holds_in_memory()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now.AddTicks(1_234_567));
        cart.SetItem(TestData.TShirt, 2, TestData.Now.AddTicks(7_654_321));
        await SaveNewAsync(cart);

        var loaded = await ReloadAsync(userId);

        Assert.Equal(cart.CreatedAt, loaded.CreatedAt);
        Assert.Equal(cart.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(cart.Items.Single().AddedAt, loaded.Items.Single().AddedAt);
    }

    [Fact]
    public async Task A_cart_without_lines_keeps_a_null_currency()
    {
        var userId = TestData.NewUserId();
        await SaveNewAsync(Cart.Create(userId, TestData.Now));

        await using var db = postgres.CreateContext();
        var loaded = await db.FindCartAsync(userId, Cancellation);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Currency);
        Assert.Empty(loaded.Items);
        Assert.Equal(0, loaded.Version);
    }

    [Fact]
    public async Task A_user_without_a_cart_has_none()
    {
        await using var db = postgres.CreateContext();

        Assert.Null(await db.FindCartAsync(TestData.NewUserId(), Cancellation));
    }

    [Fact]
    public async Task Carts_are_found_by_the_exact_user_id()
    {
        var userId = TestData.NewUserId();
        await SaveNewAsync(Cart.Create(userId, TestData.Now));

        await using var db = postgres.CreateContext();

        Assert.Null(await db.FindCartAsync(userId.ToUpperInvariant(), Cancellation));
    }

    [Fact]
    public async Task Loading_a_cart_with_its_lines_is_one_sql_statement()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        cart.SetItem(TestData.Mug, 1, TestData.Now);
        await SaveNewAsync(cart);
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<CartDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(counter)
            .Options;

        await using var db = new CartDbContext(options);
        var loaded = await db.FindCartAsync(userId, Cancellation);

        Assert.Equal(2, loaded!.Items.Count);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task A_line_added_to_a_saved_cart_is_inserted()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveNewAsync(cart);

        await using (var db = postgres.CreateContext())
        {
            var tracked = await db.FindCartAsync(userId, Cancellation);
            tracked!.SetItem(TestData.Mug, 4, TestData.Now.AddMinutes(1));
            await db.SaveChangesAsync(Cancellation);
        }

        var reloaded = await ReloadAsync(userId);
        Assert.Equal(2, reloaded.Items.Count);
        Assert.Equal(4, reloaded.Items.Single(item => item.Sku == "MUG-WHT").Quantity);
        Assert.Equal(2, reloaded.Version);
    }

    [Fact]
    public async Task A_changed_line_and_a_removed_line_are_both_saved()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        cart.SetItem(TestData.Mug, 1, TestData.Now);
        await SaveNewAsync(cart);

        await using (var db = postgres.CreateContext())
        {
            var tracked = await db.FindCartAsync(userId, Cancellation);
            tracked!.SetItem(TestData.TShirt, 5, TestData.Now.AddMinutes(1));
            tracked.RemoveItem("MUG-WHT", TestData.Now.AddMinutes(2));
            await db.SaveChangesAsync(Cancellation);
        }

        var reloaded = await ReloadAsync(userId);
        var line = Assert.Single(reloaded.Items);
        Assert.Equal("TSHIRT-BLK-M", line.Sku);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(4, reloaded.Version);
    }

    [Fact]
    public async Task A_line_removed_and_set_again_in_one_save_ends_up_with_the_new_values()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveNewAsync(cart);

        await using (var db = postgres.CreateContext())
        {
            var tracked = await db.FindCartAsync(userId, Cancellation);
            tracked!.RemoveItem("TSHIRT-BLK-M", TestData.Now.AddMinutes(1));
            tracked.SetItem(TestData.TShirt, 7, TestData.Now.AddMinutes(2));
            await db.SaveChangesAsync(Cancellation);
        }

        var reloaded = await ReloadAsync(userId);
        Assert.Equal(7, Assert.Single(reloaded.Items).Quantity);
    }

    [Fact]
    public async Task Clearing_a_saved_cart_deletes_its_lines_and_keeps_the_cart()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        cart.SetItem(TestData.Mug, 1, TestData.Now);
        await SaveNewAsync(cart);

        await using (var db = postgres.CreateContext())
        {
            var tracked = await db.FindCartAsync(userId, Cancellation);
            tracked!.Clear(TestData.Now.AddMinutes(1));
            await db.SaveChangesAsync(Cancellation);
        }

        var reloaded = await ReloadAsync(userId);
        Assert.Equal(cart.Id, reloaded.Id);
        Assert.Empty(reloaded.Items);
        Assert.Null(reloaded.Currency);
        Assert.Equal(0, await CountLinesAsync(cart.Id));
    }

    [Fact]
    public async Task A_command_that_changes_nothing_writes_nothing()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveNewAsync(cart);

        await using var db = postgres.CreateContext();
        var tracked = await db.FindCartAsync(userId, Cancellation);
        tracked!.SetItem(TestData.TShirt, 2, TestData.Now.AddMinutes(1));

        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task A_user_cannot_have_two_carts()
    {
        var userId = TestData.NewUserId();
        await SaveNewAsync(Cart.Create(userId, TestData.Now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveNewAsync(Cart.Create(userId, TestData.Now)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task Saving_over_a_cart_that_changed_in_the_meantime_is_refused()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveNewAsync(cart);
        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstCopy = await first.FindCartAsync(userId, Cancellation);
        var secondCopy = await second.FindCartAsync(userId, Cancellation);

        firstCopy!.SetItem(TestData.TShirt, 3, TestData.Now.AddMinutes(1));
        await first.SaveChangesAsync(Cancellation);
        secondCopy!.SetItem(TestData.TShirt, 9, TestData.Now.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Cancellation));
        Assert.Equal(3, Assert.Single((await ReloadAsync(userId)).Items).Quantity);
    }

    [Fact]
    public async Task Deleting_a_cart_deletes_its_lines()
    {
        var userId = TestData.NewUserId();
        var cart = Cart.Create(userId, TestData.Now);
        cart.SetItem(TestData.TShirt, 2, TestData.Now);
        await SaveNewAsync(cart);

        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlAsync($"DELETE FROM carts WHERE id = {cart.Id}", Cancellation);

        Assert.Equal(0, await CountLinesAsync(cart.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task The_database_itself_refuses_a_quantity_outside_the_range(int quantity)
    {
        var cart = Cart.Create(TestData.NewUserId(), TestData.Now);
        await SaveNewAsync(cart);
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO cart_items (cart_id, sku, product_name, unit_price, quantity, added_at) VALUES ({cart.Id}, 'MUG-WHT', 'Mug white', 7.50, {quantity}, now())",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_negative_price()
    {
        var cart = Cart.Create(TestData.NewUserId(), TestData.Now);
        await SaveNewAsync(cart);
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO cart_items (cart_id, sku, product_name, unit_price, quantity, added_at) VALUES ({cart.Id}, 'MUG-WHT', 'Mug white', -0.01, 1, now())",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_malformed_currency()
    {
        var userId = TestData.NewUserId();
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO carts (id, user_id, currency, version, created_at, updated_at) VALUES ({Guid.CreateVersion7()}, {userId}, 'eur', 0, now(), now())",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("someone", -1)]
    public async Task The_database_itself_refuses_a_cart_without_a_user_or_with_a_negative_version(string userIdOrEmpty, int version)
    {
        var userId = userIdOrEmpty.Length == 0 ? userIdOrEmpty : TestData.NewUserId();
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO carts (id, user_id, currency, version, created_at, updated_at) VALUES ({Guid.CreateVersion7()}, {userId}, NULL, {version}, now(), now())",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_line_cannot_exist_without_its_cart()
    {
        await using var db = postgres.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO cart_items (cart_id, sku, product_name, unit_price, quantity, added_at) VALUES ({Guid.CreateVersion7()}, 'MUG-WHT', 'Mug white', 7.50, 1, now())",
            Cancellation));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    private async Task SaveNewAsync(Cart cart)
    {
        await using var db = postgres.CreateContext();
        db.Carts.Add(cart);
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task<Cart> ReloadAsync(string userId)
    {
        await using var db = postgres.CreateContext();
        return (await db.FindCartAsync(userId, Cancellation))!;
    }

    private async Task<int> CountLinesAsync(Guid cartId)
    {
        await using var db = postgres.CreateContext();
        return await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cart_items WHERE cart_id = {cartId}")
            .SingleAsync(Cancellation);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
