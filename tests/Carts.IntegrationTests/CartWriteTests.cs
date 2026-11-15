using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Carts.IntegrationTests;

public class CartWriteTests(PostgresFixture postgres)
{
    private const string CartPath = "/api/v1/cart";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string ItemPath(string sku) => $"{CartPath}/items/{sku}";

    [Fact]
    public async Task Setting_an_item_adds_a_line_with_the_name_and_price_of_the_product()
    {
        var sku = await TestCatalog.AddAsync(postgres, name: "Notebook A5", unitPrice: 3.49m);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var response = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 3 }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var put = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        using var get = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        foreach (var cart in new[] { put.RootElement, get.RootElement })
        {
            Assert.Equal(1, cart.GetProperty("version").GetInt32());
            Assert.Equal("EUR", cart.GetProperty("currency").GetString());
            Assert.Equal(10.47m, cart.GetProperty("total").GetDecimal());
            var line = Assert.Single(cart.GetProperty("items").EnumerateArray());
            Assert.Equal(sku, line.GetProperty("sku").GetString());
            Assert.Equal("Notebook A5", line.GetProperty("name").GetString());
            Assert.Equal(3.49m, line.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(3, line.GetProperty("quantity").GetInt32());
            Assert.Equal(10.47m, line.GetProperty("lineTotal").GetDecimal());
        }

        Assert.Equal(put.RootElement.GetProperty("id").GetGuid(), get.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Setting_an_item_again_replaces_its_quantity_and_never_adds_to_it()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation);

        using var cart = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 5 }, Cancellation));

        Assert.Equal(5, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
        Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Sending_the_same_request_twice_changes_nothing_the_second_time()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var first = await (await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation)).Content.ReadAsStringAsync(Cancellation);
        var second = await (await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation)).Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(first, second);
        Assert.Contains("\"version\":1,", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_an_item_refreshes_the_price_it_was_added_at()
    {
        var sku = await TestCatalog.AddAsync(postgres, unitPrice: 3.49m);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation);
        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE products SET unit_price = 2.99 WHERE sku = {sku}", Cancellation);
        }

        using var cart = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation));

        Assert.Equal(2.99m, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task What_the_request_says_about_price_name_or_currency_is_ignored()
    {
        var sku = await TestCatalog.AddAsync(postgres, name: "Notebook A5", unitPrice: 3.49m);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        using var cart = await ReadAsync(await client.PutAsJsonAsync(
            ItemPath(sku),
            new { quantity = 1, unitPrice = 0.01m, name = "Free stuff", currency = "USD", total = 0, userId = "someone-else" },
            Cancellation));

        var line = Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(3.49m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Notebook A5", line.GetProperty("name").GetString());
        Assert.Equal("EUR", cart.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task One_users_changes_never_touch_another_users_cart()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var alice = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        using var bob = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await alice.PutAsJsonAsync(ItemPath(sku), new { quantity = 2 }, Cancellation);

        using var bobsCart = JsonDocument.Parse(await bob.GetStringAsync(CartPath, Cancellation));
        await bob.DeleteAsync(ItemPath(sku), Cancellation);
        await bob.DeleteAsync(CartPath, Cancellation);

        Assert.Equal(0, bobsCart.RootElement.GetProperty("items").GetArrayLength());
        using var alicesCart = JsonDocument.Parse(await alice.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(2, Assert.Single(alicesCart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
        Assert.Equal(1, alicesCart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_product_that_does_not_exist_answers_404_and_creates_no_cart()
    {
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var response = await client.PutAsJsonAsync(ItemPath("NO-SUCH-PRODUCT"), new { quantity = 1 }, Cancellation);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "product_not_found");
        Assert.False(await HasCartAsync(userId));
    }

    [Fact]
    public async Task A_product_that_is_no_longer_sold_answers_422_and_a_refused_first_request_stores_no_cart()
    {
        var sku = await TestCatalog.AddAsync(postgres, isActive: false);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        using var problem = await AssertProblemAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation), HttpStatusCode.UnprocessableEntity, "product_unavailable");

        Assert.False(await HasCartAsync(userId));
    }

    [Fact]
    public async Task A_product_that_stopped_being_sold_can_still_be_removed()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);
        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE products SET is_active = false WHERE sku = {sku}", Cancellation);
        }

        var response = await client.DeleteAsync(ItemPath(sku), Cancellation);

        using var cart = await ReadAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_product_row_the_cart_cannot_use_is_reported_and_answered_as_not_found_instead_of_failing()
    {
        // The table allows a name made of a non-breaking space; the cart's own rule does not.
        var sku = $"P-{Guid.NewGuid():N}".ToUpperInvariant();
        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO products (sku, name, unit_price, currency, is_active) VALUES ({sku}, {"\u00A0"}, 1, 'EUR', true)",
                Cancellation);
        }

        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        await AssertProblemAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation), HttpStatusCode.NotFound, "product_not_found");
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Message.Contains(sku, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_change_stamps_the_cart_with_the_time_it_happened()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var other = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        using var afterSet = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation));
        await client.PutAsJsonAsync(ItemPath(other), new { quantity = 1 }, Cancellation);
        using var afterRemove = await ReadAsync(await client.DeleteAsync(ItemPath(other), Cancellation));
        using var afterClear = await ReadAsync(await client.DeleteAsync(CartPath, Cancellation));

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        var times = new[] { afterSet, afterRemove, afterClear }.Select(cart => cart.RootElement.GetProperty("updatedAt").GetDateTimeOffset()).ToArray();
        Assert.All(times, time => Assert.InRange(time, before, after));
        Assert.True(times[0] <= times[1] && times[1] <= times[2], "A later change must not carry an earlier time.");
        Assert.All(times, time => Assert.Equal(TimeSpan.Zero, time.Offset));
    }

    [Fact]
    public async Task The_same_field_given_twice_in_a_body_is_refused()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var exact = await client.PutAsync(ItemPath(sku), Json("""{ "quantity": 7, "quantity": 8 }"""), Cancellation);
        var byCase = await client.PutAsync(ItemPath(sku), Json("""{ "quantity": 7, "Quantity": 8 }"""), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, exact.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, byCase.StatusCode);
        Assert.False(await HasCartAsync(userId));
    }

    [Fact]
    public async Task A_bad_sku_and_a_bad_quantity_are_reported_together_with_the_actual_limits()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        using var problem = await AssertProblemAsync(
            await client.PutAsJsonAsync(ItemPath("-bad"), new { quantity = 0 }, Cancellation),
            HttpStatusCode.BadRequest,
            "validation_failed");

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Contains("1 to 99", errors.GetProperty("quantity")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains("1 to 64", errors.GetProperty("sku")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_in_another_currency_than_the_cart_answers_422_and_leaves_the_cart_alone()
    {
        var euro = await TestCatalog.AddAsync(postgres, currency: "EUR");
        var dollar = await TestCatalog.AddAsync(postgres, currency: "USD");
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(euro), new { quantity = 1 }, Cancellation);

        await AssertProblemAsync(await client.PutAsJsonAsync(ItemPath(dollar), new { quantity = 1 }, Cancellation), HttpStatusCode.UnprocessableEntity, "currency_mismatch");

        using var cart = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(1, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task The_fifty_first_different_product_answers_422()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        for (var number = 1; number <= 50; number++)
        {
            var sku = await TestCatalog.AddAsync(postgres);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation)).StatusCode);
        }

        var oneTooMany = await TestCatalog.AddAsync(postgres);

        using var problem = await AssertProblemAsync(await client.PutAsJsonAsync(ItemPath(oneTooMany), new { quantity = 1 }, Cancellation), HttpStatusCode.UnprocessableEntity, "item_limit_exceeded");
        Assert.Contains("50", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("Too many different products", problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("""{ "quantity": 0 }""")]
    [InlineData("""{ "quantity": 100 }""")]
    [InlineData("""{ "quantity": null }""")]
    [InlineData("{}")]
    public async Task A_quantity_that_is_missing_or_outside_1_to_99_is_a_validation_error(string json)
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var response = await client.PutAsync(ItemPath(sku), Json(json), Cancellation);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("quantity", out _));
        Assert.False(await HasCartAsync(userId));
    }

    [Theory]
    [InlineData("""{ "quantity": 1 }""", 1)]
    [InlineData("""{ "quantity": 99 }""", 99)]
    public async Task The_smallest_and_the_largest_quantity_are_accepted(string json, int expected)
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        using var cart = await ReadAsync(await client.PutAsync(ItemPath(sku), Json(json), Cancellation));

        Assert.Equal(expected, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData("""{ "quantity": "2" }""")]
    [InlineData("""{ "quantity": 1.5 }""")]
    [InlineData("""{ "quantity": 2""")]
    [InlineData("")]
    [InlineData("null")]
    public async Task A_body_that_is_not_the_expected_json_answers_400_and_changes_nothing(string json)
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        var response = await client.PutAsync(ItemPath(sku), Json(json), Cancellation);

        // Its own code, so a client can tell it from a value that breaks a rule. The detail
        // says what is wrong without naming the API's own types.
        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_body");
        var detail = problem.RootElement.GetProperty("detail").GetString();
        Assert.Contains("JSON", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Request", detail, StringComparison.Ordinal);
        Assert.False(await HasCartAsync(userId));
    }

    [Theory]
    [InlineData("has%20space")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_malformed_sku_is_a_validation_error_on_every_route_and_never_reaches_the_database(string sku)
    {
        // The database here refuses every connection, so any answer other than 400 would mean it was asked.
        await using var factory = new CartApiFactory("Host=127.0.0.1;Port=1;Database=carts;Username=carts;Password=unused");
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var put = await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);
        var delete = await client.DeleteAsync(ItemPath(sku), Cancellation);

        using var putProblem = await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(putProblem.RootElement.GetProperty("errors").TryGetProperty("sku", out _));
        using var deleteProblem = await AssertProblemAsync(delete, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(deleteProblem.RootElement.GetProperty("errors").TryGetProperty("sku", out _));
    }

    [Fact]
    public async Task Removing_an_item_removes_exactly_that_line()
    {
        var keep = await TestCatalog.AddAsync(postgres);
        var remove = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(keep), new { quantity = 1 }, Cancellation);
        await client.PutAsJsonAsync(ItemPath(remove), new { quantity = 2 }, Cancellation);

        var response = await client.DeleteAsync(ItemPath(remove), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var cart = await ReadAsync(response);
        Assert.Equal(keep, Assert.Single(cart.RootElement.GetProperty("items").EnumerateArray()).GetProperty("sku").GetString());
        Assert.Equal(3, cart.RootElement.GetProperty("version").GetInt32());
        using var stored = JsonDocument.Parse(await client.GetStringAsync(CartPath, Cancellation));
        Assert.Equal(1, stored.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Removing_the_last_item_leaves_an_empty_cart_without_a_currency()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        using var before = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation));

        using var cart = await ReadAsync(await client.DeleteAsync(ItemPath(sku), Cancellation));

        Assert.Equal(0, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, cart.RootElement.GetProperty("currency").ValueKind);
        Assert.Equal(0m, cart.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(before.RootElement.GetProperty("id").GetGuid(), cart.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(2, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Removing_an_item_twice_answers_200_both_times_and_changes_nothing_the_second_time()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);

        var first = await client.DeleteAsync(ItemPath(sku), Cancellation);
        var second = await client.DeleteAsync(ItemPath(sku), Cancellation);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Cancellation), await second.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task Removing_an_item_that_is_not_a_product_at_all_is_fine_too()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);

        using var cart = await ReadAsync(await client.DeleteAsync(ItemPath("NO-SUCH-PRODUCT"), Cancellation));

        Assert.Equal(1, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, cart.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Clearing_the_cart_removes_every_line_and_keeps_the_cart()
    {
        var first = await TestCatalog.AddAsync(postgres);
        var second = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        await client.PutAsJsonAsync(ItemPath(first), new { quantity = 1 }, Cancellation);
        using var before = await ReadAsync(await client.PutAsJsonAsync(ItemPath(second), new { quantity = 2 }, Cancellation));

        var response = await client.DeleteAsync(CartPath, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var cart = await ReadAsync(response);
        Assert.Equal(0, cart.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, cart.RootElement.GetProperty("currency").ValueKind);
        Assert.Equal(before.RootElement.GetProperty("id").GetGuid(), cart.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(3, cart.RootElement.GetProperty("version").GetInt32());
        using var again = await ReadAsync(await client.DeleteAsync(CartPath, Cancellation));
        Assert.Equal(3, again.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Removing_and_clearing_for_a_user_without_a_cart_answer_the_empty_cart_and_store_nothing()
    {
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);
        const string empty = """{"id":null,"version":0,"currency":null,"items":[],"total":0,"updatedAt":null}""";

        var removed = await client.DeleteAsync(ItemPath("SOME-SKU"), Cancellation);
        var cleared = await client.DeleteAsync(CartPath, Cancellation);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(empty, await removed.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(empty, await cleared.Content.ReadAsStringAsync(Cancellation));
        Assert.False(await HasCartAsync(userId));
    }

    [Fact]
    public async Task Every_cart_route_needs_a_token()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(ItemPath("ANY"), new { quantity = 1 }, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(ItemPath("ANY"), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(CartPath, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task No_answer_about_a_cart_is_ever_stored_by_a_cache()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var answers = new[]
        {
            await client.GetAsync(CartPath, Cancellation),
            await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation),
            await client.PutAsJsonAsync(ItemPath("NO-SUCH-PRODUCT"), new { quantity = 1 }, Cancellation),
            await client.DeleteAsync(ItemPath(sku), Cancellation),
            await client.DeleteAsync(CartPath, Cancellation),
        };

        Assert.All(answers, answer => Assert.True(answer.Headers.CacheControl?.NoStore));
    }

    [Fact]
    public async Task Answers_raised_before_the_cart_is_even_looked_at_are_not_cached_either()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var anonymous = factory.CreateClient();
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        using var form = new StringContent("quantity=1", Encoding.UTF8, "application/x-www-form-urlencoded");

        var answers = new Dictionary<string, HttpResponseMessage>
        {
            ["401"] = await anonymous.GetAsync(CartPath, Cancellation),
            ["405"] = await client.PostAsync(CartPath, content: null, Cancellation),
            ["415"] = await client.PutAsync(ItemPath(sku), form, Cancellation),
            ["400 unreadable body"] = await client.PutAsync(ItemPath(sku), Json("""{ "quantity": "2" }"""), Cancellation),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, answers["401"].StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, answers["405"].StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answers["415"].StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, answers["400 unreadable body"].StatusCode);
        Assert.All(answers, answer => Assert.True(answer.Value.Headers.CacheControl?.NoStore, $"{answer.Key} may be cached."));
    }

    [Fact]
    public async Task A_change_is_logged_with_the_cart_and_the_user_and_nothing_private()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        using var cart = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation));

        var cartId = cart.RootElement.GetProperty("id").GetGuid().ToString();
        var entry = Assert.Single(factory.Logs.Entries, entry =>
            entry.Category.StartsWith("Carts.Api", StringComparison.Ordinal)
            && entry.Message.Contains(cartId, StringComparison.Ordinal));
        Assert.Equal(userId, entry.Scope["UserId"]);
        Assert.Contains("version 1", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(client.DefaultRequestHeaders.Authorization!.Parameter!, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_changes_nothing_is_not_logged_as_a_change()
    {
        var sku = await TestCatalog.AddAsync(postgres);
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);
        using var cart = await ReadAsync(await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation));
        var cartId = cart.RootElement.GetProperty("id").GetGuid().ToString();

        await client.PutAsJsonAsync(ItemPath(sku), new { quantity = 1 }, Cancellation);
        await client.DeleteAsync(ItemPath("NOT-IN-THE-CART"), Cancellation);

        Assert.Single(factory.Logs.Entries, entry => entry.Message.Contains(cartId, StringComparison.Ordinal));
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(body);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());

        // Every status this API answers with is defined in the current HTTP standard.
        Assert.StartsWith("https://tools.ietf.org/html/rfc9110#section-15.", problem.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        return problem;
    }

    private async Task<bool> HasCartAsync(string userId)
    {
        await using var db = postgres.CreateContext();
        return await db.Carts.AnyAsync(cart => cart.UserId == userId, Cancellation);
    }
}
