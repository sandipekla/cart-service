using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carts.Api.Persistence;
using Carts.Api.Products;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carts.IntegrationTests;

/// <summary>
/// Requests in these tests are truly parallel: every one is started before any is awaited,
/// and all are released by one signal.
/// </summary>
public class CartConcurrencyTests(PostgresFixture postgres)
{
    private const string CartPath = "/api/v1/cart";

    /// <summary>Long enough that a slow machine never turns a waiting request into a busy one.</summary>
    private static readonly Dictionary<string, string?> PatientLock = new() { ["Cart:LockTimeoutMs"] = "30000" };

    private static readonly Dictionary<string, string?> ImpatientLock = new() { ["Cart:LockTimeoutMs"] = "200" };

    /// <summary>A request sent while the test holds the lock fails its test if it is never answered; it does not hang it.</summary>
    private static readonly TimeSpan GivingUp = TimeSpan.FromSeconds(20);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string ItemPath(string sku) => $"{CartPath}/items/{sku}";

    [Fact]
    public async Task Twenty_parallel_requests_for_twenty_products_all_succeed()
    {
        var skus = await AddProductsAsync(20);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var answers = await SendTogetherAsync(skus.Select(sku => Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation))));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        var versions = await VersionsOfAsync(answers);
        Assert.Equal(Enumerable.Range(1, 20), versions.Order());
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(20, cart.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(skus.Order(StringComparer.Ordinal), SkusOf(cart).Order(StringComparer.Ordinal));
        Assert.Equal(1, await CountCartsAsync(userId));
    }

    [Fact]
    public async Task Ten_parallel_first_requests_of_a_new_user_create_exactly_one_cart()
    {
        var skus = await AddProductsAsync(10);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var answers = await SendTogetherAsync(skus.Select(sku => Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation))));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        Assert.Equal(1, await CountCartsAsync(userId));
        var ids = new HashSet<Guid>();
        foreach (var answer in answers)
        {
            using var body = await ReadAsync(answer);
            ids.Add(body.RootElement.GetProperty("id").GetGuid());
        }

        Assert.Single(ids);
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(10, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(10, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task The_same_request_sent_ten_times_at_once_changes_the_cart_once()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var answers = await SendTogetherAsync(Enumerable.Range(0, 10).Select(_ => Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 3 }, Cancellation))));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        Assert.All(await VersionsOfAsync(answers), version => Assert.Equal(1, version));
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(3, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
        Assert.Equal(1, await CountCartsAsync(userId));
    }

    [Fact]
    public async Task Parallel_requests_setting_one_product_to_2_and_to_5_leave_the_later_of_the_two()
    {
        // Ten users at once, so that the two requests of a user really meet.
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        var clients = new List<HttpClient>();
        for (var number = 0; number < 10; number++)
        {
            clients.Add(await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation));
        }

        var answers = await SendTogetherAsync(clients.SelectMany(client => new[]
        {
            Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation)),
            Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 5 }, Cancellation)),
        }));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        for (var number = 0; number < clients.Count; number++)
        {
            using var two = await ReadAsync(answers[number * 2]);
            using var five = await ReadAsync(answers[(number * 2) + 1]);
            using var cart = JsonDocument.Parse(await clients[number].GetStringAsync(CartPath, Cancellation));

            // Both were real changes, one after the other: versions 1 and 2. The later one stands.
            var later = two.RootElement.GetProperty("version").GetInt32() == 2 ? 2 : 5;
            Assert.Equal(3, two.RootElement.GetProperty("version").GetInt32() + five.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(later, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
        }

        clients.ForEach(client => client.Dispose());
    }

    [Fact]
    public async Task Parallel_sets_removes_and_a_clear_all_succeed_and_leave_a_consistent_cart()
    {
        var existing = await AddProductsAsync(5);
        var added = await AddProductsAsync(10);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        foreach (var sku in existing)
        {
            await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);
        }

        var sets = added.Select(sku => Send(() => client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation))).ToArray();
        var removes = existing.Select(sku => Send(() => client.DeleteAsync(ItemPath(sku), Cancellation)));
        var clear = Send(() => client.DeleteAsync(CartPath, Cancellation));
        var answers = await SendTogetherAsync([.. sets, .. removes, clear]);

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));

        // Every set adds a product, so each is a real change with a version of its own.
        var setVersions = await VersionsOfAsync(answers[..sets.Length]);
        Assert.Equal(sets.Length, setVersions.Distinct().Count());

        // The cart as stored is the cart of the answer with the highest version.
        var stored = await client.GetStringAsync(CartPath, Cancellation);
        var bodies = new List<string>();
        foreach (var answer in answers)
        {
            bodies.Add(await answer.Content.ReadAsStringAsync(Cancellation));
        }

        var last = bodies.MaxBy(body => JsonDocument.Parse(body).RootElement.GetProperty("version").GetInt32());
        Assert.Equal(last, stored);
        using var cart = JsonDocument.Parse(stored);
        var skus = SkusOf(cart);
        Assert.Equal(skus.Count, skus.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            cart.RootElement.GetProperty("items").EnumerateArray().Sum(line => line.GetProperty("lineTotal").GetDecimal()),
            cart.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(1, await CountCartsAsync(userId));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE item")]
    [InlineData("DELETE cart")]
    public async Task A_cart_locked_elsewhere_past_the_timeout_answers_409_cart_busy_and_changes_nothing(string change)
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: ImpatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        var before = await (await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation)).Content.ReadAsStringAsync(Cancellation);

        HttpResponseMessage answer;
        await using (await CartLock.HoldAsync(postgres.ConnectionString, userId, Cancellation))
        {
            answer = change switch
            {
                "PUT" => await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 9 }, Cancellation).WaitAsync(GivingUp, Cancellation),
                "DELETE item" => await client.DeleteAsync(ItemPath(sku), Cancellation).WaitAsync(GivingUp, Cancellation),
                _ => await client.DeleteAsync(CartPath, Cancellation).WaitAsync(GivingUp, Cancellation),
            };
        }

        var body = await answer.Content.ReadAsStringAsync(Cancellation);
        Assert.True(answer.StatusCode == HttpStatusCode.Conflict, $"Expected 409, got {(int)answer.StatusCode}: {body}");
        Assert.Equal("application/problem+json", answer.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("cart_busy", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(TimeSpan.FromSeconds(1), answer.Headers.RetryAfter?.Delta);
        Assert.True(answer.Headers.CacheControl?.NoStore);
        Assert.Equal(before, await client.GetStringAsync(CartPath, Cancellation));
    }

    [Fact]
    public async Task A_busy_cart_is_logged_once_as_a_warning_and_never_as_an_error()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: ImpatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        await using (await CartLock.HoldAsync(postgres.ConnectionString, userId, Cancellation))
        {
            await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation).WaitAsync(GivingUp, Cancellation);
        }

        var entry = Assert.Single(factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning && entry.Scope.ContainsKey("UserId"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("Carts.Api", entry.Category, StringComparison.Ordinal);
        Assert.Equal(userId, entry.Scope["UserId"]);
        Assert.Contains("200", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cart_row_locked_elsewhere_past_the_timeout_is_a_busy_cart_too()
    {
        // The timeout covers every wait of the change, not only the wait for the cart lock.
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: ImpatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        var before = await (await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation)).Content.ReadAsStringAsync(Cancellation);

        HttpResponseMessage answer;
        await using (await CartLock.HoldRowAsync(postgres.ConnectionString, userId, Cancellation))
        {
            answer = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 9 }, Cancellation).WaitAsync(GivingUp, Cancellation);
        }

        await AssertBusyAsync(answer, factory, userId);
        Assert.Equal(before, await client.GetStringAsync(CartPath, Cancellation));
    }

    [Fact]
    public async Task A_carts_table_locked_elsewhere_past_the_timeout_is_a_busy_cart_too()
    {
        // A database of its own: locking the table would stall every other test.
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        string sku;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Database.MigrateAsync(Cancellation);
            var product = new Product($"P-{Guid.NewGuid():N}".ToUpperInvariant(), "Notebook A5", 3.49m, "EUR", isActive: true);
            db.Products.Add(product);
            await db.SaveChangesAsync(Cancellation);
            sku = product.Sku;
        }

        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(connectionString, settings: ImpatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        HttpResponseMessage answer;
        await using (await CartLock.HoldTableAsync(connectionString, Cancellation))
        {
            answer = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation).WaitAsync(GivingUp, Cancellation);
        }

        await AssertBusyAsync(answer, factory, userId);
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(0, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_request_waits_for_the_lock_and_succeeds_once_it_is_free()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        Task<HttpResponseMessage> waiting;
        await using (await CartLock.HoldAsync(postgres.ConnectionString, userId, Cancellation))
        {
            waiting = client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);

            // Longer than the default timeout of two seconds: the configured one is what counts.
            await Task.Delay(TimeSpan.FromMilliseconds(2500), Cancellation);
            Assert.False(waiting.IsCompleted, "The request did not wait for the cart lock as long as it was configured to.");
        }

        Assert.Equal(HttpStatusCode.OK, (await waiting.WaitAsync(GivingUp, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Reading_a_cart_and_changing_another_users_cart_never_wait_for_the_lock()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: ImpatientLock);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        using var someoneElse = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        await using (await CartLock.HoldAsync(postgres.ConnectionString, userId, Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(CartPath, Cancellation).WaitAsync(GivingUp, Cancellation)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await someoneElse.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation).WaitAsync(GivingUp, Cancellation)).StatusCode);

            // A product that does not exist is answered without the lock as well.
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(ItemPath("NO-SUCH-PRODUCT"), new { quantity = 1 }, Cancellation).WaitAsync(GivingUp, Cancellation)).StatusCode);
        }
    }

    [Fact]
    public async Task A_writer_that_ignored_the_lock_is_noticed_and_answered_409_instead_of_being_overwritten()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        var intruder = new WriterIgnoringTheLock(postgres.ConnectionString, userId, () =>
        {
            using var db = postgres.CreateContext();
            db.Database.ExecuteSql($"UPDATE carts SET version = version + 1 WHERE user_id = {userId}");
        });
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock, time: intruder);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);

        intruder.Arm();
        var answer = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 9 }, Cancellation);

        await AssertConflictAsync(answer, factory, userId);
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(1, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
        Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_cart_created_by_a_writer_that_ignored_the_lock_is_noticed_too()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        var intruder = new WriterIgnoringTheLock(postgres.ConnectionString, userId, () =>
        {
            using var db = postgres.CreateContext();
            db.Carts.Add(Cart.Create(userId, DateTimeOffset.UtcNow));
            db.SaveChanges();
        });
        await using var factory = new CartApiFactory(postgres.ConnectionString, settings: PatientLock, time: intruder);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        intruder.Arm();
        var answer = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);

        await AssertConflictAsync(answer, factory, userId);
        Assert.Equal(1, await CountCartsAsync(userId));
        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(0, cart.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_lock_timeout_is_set_for_the_transaction_and_gone_after_it()
    {
        await using var db = postgres.CreateContext();
        await db.Database.OpenConnectionAsync(Cancellation);
        var before = await LockTimeoutAsync(db);

        string during;
        await using (var transaction = await db.Database.BeginTransactionAsync(Cancellation))
        {
            await db.LockCartAsync(TestData.NewUserId(), TimeSpan.FromMilliseconds(1234), Cancellation);
            during = await LockTimeoutAsync(db);
            await transaction.CommitAsync(Cancellation);
        }

        Assert.Equal("1234ms", during);
        Assert.Equal(before, await LockTimeoutAsync(db));
        Assert.NotEqual(during, before);
    }

    [Fact]
    public async Task The_lock_is_released_when_the_transaction_ends_without_a_commit()
    {
        var userId = TestData.NewUserId();
        await using var db = postgres.CreateContext();

        await using (await db.Database.BeginTransactionAsync(Cancellation))
        {
            await db.LockCartAsync(userId, TimeSpan.FromSeconds(30), Cancellation);
            Assert.True(CartLock.IsHeld(postgres.ConnectionString, userId));
        }

        Assert.False(CartLock.IsHeld(postgres.ConnectionString, userId));
    }

    [Fact]
    public async Task Taking_the_lock_outside_a_transaction_is_refused()
    {
        await using var db = postgres.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.LockCartAsync(TestData.NewUserId(), TimeSpan.FromSeconds(1), Cancellation));
    }

    [Fact]
    public async Task Taking_the_lock_with_a_timeout_under_a_millisecond_is_refused()
    {
        // PostgreSQL reads a lock timeout of zero as "wait for ever".
        await using var db = postgres.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Cancellation);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.LockCartAsync(TestData.NewUserId(), TimeSpan.FromMicroseconds(999), Cancellation));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("60001")]
    public void The_api_does_not_start_with_a_lock_timeout_outside_its_range(string value)
    {
        using var factory = new CartApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?> { ["Cart:LockTimeoutMs"] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Cart:LockTimeoutMs", exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertBusyAsync(HttpResponseMessage answer, CartApiFactory factory, string userId)
    {
        var body = await answer.Content.ReadAsStringAsync(Cancellation);
        Assert.True(answer.StatusCode == HttpStatusCode.Conflict, $"Expected 409, got {(int)answer.StatusCode}: {body}");
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("cart_busy", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(TimeSpan.FromSeconds(1), answer.Headers.RetryAfter?.Delta);

        // One warning from the API itself, and no second entry from the database layer.
        var entry = Assert.Single(factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning && entry.Scope.ContainsKey("UserId"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("Carts.Api", entry.Category, StringComparison.Ordinal);
        Assert.Equal(userId, entry.Scope["UserId"]);
    }

    private static async Task AssertConflictAsync(HttpResponseMessage answer, CartApiFactory factory, string userId)
    {
        var body = await answer.Content.ReadAsStringAsync(Cancellation);
        Assert.True(answer.StatusCode == HttpStatusCode.Conflict, $"Expected 409, got {(int)answer.StatusCode}: {body}");
        Assert.Equal("application/problem+json", answer.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("concurrency_conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.Null(answer.Headers.RetryAfter);

        // Reported once, by the API itself, as an error: it means a write path has a bug.
        var entry = Assert.Single(factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning && entry.Scope.ContainsKey("UserId"));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.StartsWith("Carts.Api", entry.Category, StringComparison.Ordinal);
        Assert.Equal(userId, entry.Scope["UserId"]);
    }

    /// <summary>A request that is not sent yet. It only gives the lambda its type.</summary>
    private static Func<Task<HttpResponseMessage>> Send(Func<Task<HttpResponseMessage>> request) => request;

    /// <summary>Starts every request on its own task, releases them with one signal and waits for all.</summary>
    private static async Task<HttpResponseMessage[]> SendTogetherAsync(IEnumerable<Func<Task<HttpResponseMessage>>> requests)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = requests
            .Select(request => Task.Run(
                async () =>
                {
                    await start.Task;
                    return await request();
                },
                Cancellation))
            .ToArray();

        start.SetResult();

        return await Task.WhenAll(running);
    }

    private static async Task<List<int>> VersionsOfAsync(IEnumerable<HttpResponseMessage> answers)
    {
        var versions = new List<int>();
        foreach (var answer in answers)
        {
            using var body = await ReadAsync(answer);
            versions.Add(body.RootElement.GetProperty("version").GetInt32());
        }

        return versions;
    }

    private static List<string> SkusOf(JsonDocument cart) =>
        [.. cart.RootElement.GetProperty("items").EnumerateArray().Select(line => line.GetProperty("sku").GetString()!)];

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

    private static async Task<string> LockTimeoutAsync(CartDbContext db) =>
        await db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(Cancellation);

    private async Task<List<string>> AddProductsAsync(int count)
    {
        var skus = new List<string>();
        for (var number = 0; number < count; number++)
        {
            skus.Add(await TestCatalog.AddAsync(postgres));
        }

        return skus;
    }

    private async Task<int> CountCartsAsync(string userId)
    {
        await using var db = postgres.CreateContext();
        return await db.Carts.CountAsync(cart => cart.UserId == userId, Cancellation);
    }
}
