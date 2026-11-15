using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Carts.IntegrationTests;

public class DevTokenEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_from_one_run_of_the_api_does_not_work_in_another()
    {
        await using var first = new CartApiFactory(postgres.ConnectionString);
        await using var second = new CartApiFactory(postgres.ConnectionString);
        using var clientOfFirst = await first.CreateClientForAsync("alice", Cancellation);
        using var clientOfSecond = second.CreateClient();
        clientOfSecond.DefaultRequestHeaders.Authorization = clientOfFirst.DefaultRequestHeaders.Authorization;

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientOfSecond.GetAsync("/api/v1/cart", Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "userId": "" }""")]
    [InlineData("""{ "userId": "has space" }""")]
    [InlineData("""{ "userId": "-starts-with-a-hyphen" }""")]
    [InlineData("""{ "userId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }""")]
    public async Task A_missing_or_malformed_user_id_is_a_validation_error(string json)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/dev/token", content, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("userId", out _));
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{\"userId\":")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"userId\":123}")]
    public async Task A_body_that_is_not_the_expected_json_is_the_callers_error_not_ours(string body)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/dev/token", content, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("invalid_body", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_body_that_is_not_json_by_its_content_type_answers_415()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var content = new StringContent("userId=alice", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await client.PostAsync("/dev/token", content, Cancellation);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a.b_c-1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Every_user_id_the_rule_allows_gets_a_token(string userId)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/dev/token", new { userId }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task It_issues_a_bearer_token_for_an_hour_that_the_api_accepts_and_no_cache_keeps()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/dev/token", new { userId = "alice" }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(3600, body.RootElement.GetProperty("expiresIn").GetInt32());
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(body.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(body.RootElement.GetProperty("expiresIn").GetInt32(), (int)(token.ValidTo - token.ValidFrom).TotalSeconds);
        Assert.Equal("alice", token.Subject);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.EncodedToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cart", Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("Production", "/dev/token")]
    [InlineData("Production", "/openapi/v1.json")]
    [InlineData("Production", "/scalar")]
    [InlineData("Staging", "/dev/token")]
    [InlineData("Staging", "/scalar")]
    public async Task Outside_development_the_development_endpoints_do_not_exist(string environment, string path)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString, environment);
        using var client = factory.CreateClient();

        var response = path == "/dev/token"
            ? await client.PostAsJsonAsync(path, new { userId = "alice" }, Cancellation)
            : await client.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
