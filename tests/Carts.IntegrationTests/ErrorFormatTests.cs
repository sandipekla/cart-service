using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Carts.IntegrationTests;

public class ErrorFormatTests(PostgresFixture postgres)
{
    private const string RefusingDatabase = "Host=127.0.0.1;Port=1;Database=carts;Username=carts;Password=unused";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unknown_route_answers_404_in_the_common_error_format()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/no-such-thing", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_method_the_route_does_not_support_answers_405_in_the_common_error_format()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var response = await client.PostAsync("/api/v1/cart", content: null, Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_failure_inside_the_api_answers_500_and_says_nothing_about_its_cause()
    {
        await using var factory = new CartApiFactory(RefusingDatabase);
        using var client = await factory.CreateClientForAsync(TestData.NewUserId(), Cancellation);

        var response = await client.GetAsync("/api/v1/cart", Cancellation);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.False(problem.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task What_is_logged_during_a_request_names_the_user()
    {
        var userId = TestData.NewUserId();
        await using var factory = new CartApiFactory(RefusingDatabase);
        using var client = await factory.CreateClientForAsync(userId, Cancellation);

        await client.GetAsync("/api/v1/cart", Cancellation);

        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Level >= LogLevel.Error
            && entry.Scope.TryGetValue("UserId", out var loggedUser)
            && Equals(loggedUser, userId));
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Level == LogLevel.Error
            && entry.Category.StartsWith("Carts.Api", StringComparison.Ordinal)
            && entry.Message.Contains(userId, StringComparison.Ordinal));
    }
}
