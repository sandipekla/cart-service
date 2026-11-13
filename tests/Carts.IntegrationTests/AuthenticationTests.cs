using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Carts.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carts.IntegrationTests;

public class AuthenticationTests(PostgresFixture postgres)
{
    private const string CartPath = "/api/v1/cart";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_without_a_token_is_refused_in_the_common_error_format()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(CartPath, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task A_valid_token_is_accepted()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: TestData.NewUserId())));
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, expiresIn: TimeSpan.FromMinutes(-5))));
    }

    [Fact]
    public async Task A_token_that_expired_two_minutes_ago_is_refused_and_one_ten_seconds_ago_is_still_within_tolerance()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, expiresIn: TimeSpan.FromMinutes(-2))));
        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: TestData.NewUserId(), expiresIn: TimeSpan.FromSeconds(-10))));
    }

    [Fact]
    public async Task A_token_that_never_expires_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, withExpiry: false)));
    }

    [Theory]
    [InlineData("another-api")]
    [InlineData("cart-api/")]
    [InlineData("CART-API")]
    public async Task A_token_for_an_audience_that_only_resembles_ours_is_refused(string audience)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, audience: audience)));
    }

    [Theory]
    [InlineData("ali\0ce")]
    [InlineData("alice\n")]
    [InlineData("\u0007")]
    public async Task A_user_id_with_a_control_character_is_refused(string subject)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: subject)));
    }

    [Fact]
    public async Task The_refusal_does_not_explain_what_was_wrong_with_the_token()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(factory, expiresIn: TimeSpan.FromMinutes(-5)));

        var response = await client.GetAsync(CartPath, Cancellation);

        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.DoesNotContain("expired", challenge.Parameter ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge.Parameter ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:ValidAudiences:0", "another-api")]
    [InlineData("Authentication:Schemes:Bearer:ValidAudience", "another-api")]
    public async Task Configuration_cannot_add_an_audience_the_code_does_not_name(string key, string value)
    {
        await using var factory = new CartApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string?> { [key] = value });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, audience: "another-api")));
    }

    [Fact]
    public async Task Configuration_cannot_add_an_issuer_the_code_does_not_name()
    {
        await using var factory = new CartApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string?> { ["Authentication:Schemes:Bearer:ValidIssuers:0"] = "someone-else" });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, issuer: "someone-else")));
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, audience: "another-api")));
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, issuer: "someone-else")));
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, signingKey: TestTokens.SomeOtherKey())));
    }

    [Fact]
    public async Task A_token_without_a_signature_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, signed: false)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_token_that_names_no_user_is_refused(string? subject)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: subject)));
    }

    [Fact]
    public async Task A_user_id_may_be_as_long_as_the_cart_allows_but_no_longer()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        var unique = Guid.NewGuid().ToString("N");
        var atTheLimit = unique + new string('u', 255 - unique.Length);

        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: atTheLimit)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, TestTokens.Create(factory, subject: atTheLimit + "u")));
    }

    [Fact]
    public async Task Something_that_is_not_a_token_is_refused()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, "not-a-token"));
    }

    [Fact]
    public async Task Outside_development_a_token_signed_with_a_shared_secret_is_refused()
    {
        await using var development = new CartApiFactory(postgres.ConnectionString);
        var developmentToken = TestTokens.Create(development);
        await using var production = new CartApiFactory(postgres.ConnectionString, environment: "Production");

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(production, developmentToken));
    }

    [Theory]
    [InlineData("Production", null, "cart-api", "Auth:Authority")]
    [InlineData("Production", "http://identity.invalid", "cart-api", "Auth:Authority")]
    [InlineData("Production", "not a url", "cart-api", "Auth:Authority")]
    [InlineData("Production", " https://identity.invalid ", "cart-api", "Auth:Authority")]
    [InlineData("Production", CartApiFactory.SomeAuthority, null, "Auth:Audience")]
    [InlineData("Production", CartApiFactory.SomeAuthority, "  ", "Auth:Audience")]
    [InlineData("Staging", null, "cart-api", "Auth:Authority")]
    public void Outside_development_the_api_does_not_start_without_an_identity_provider(string environment, string? authority, string? audience, string expectedInMessage)
    {
        using var factory = new CartApiFactory(postgres.ConnectionString, environment, authority: authority, audience: audience);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_says_loudly_at_startup_that_its_tokens_are_not_for_production()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Development authentication", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_does_not_log_the_development_warning()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString, environment: "Production");
        using var client = factory.CreateClient();

        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains("Development authentication", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Every_endpoint_says_whether_it_needs_a_token(string environment)
    {
        using var factory = new CartApiFactory(postgres.ConnectionString, environment);
        using var client = factory.CreateClient();

        var undecided = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(endpoint => endpoint.DisplayName);

        Assert.Empty(undecided);
    }

    [Fact]
    public void Outside_development_only_the_health_endpoints_are_open()
    {
        using var factory = new CartApiFactory(postgres.ConnectionString, environment: "Production");
        using var client = factory.CreateClient();

        Assert.Equal(["/health/live", "/health/ready"], OpenRoutes(factory));
    }

    [Fact]
    public void In_development_the_token_endpoint_and_the_documentation_are_open_as_well()
    {
        using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var open = OpenRoutes(factory);

        Assert.Contains("/dev/token", open);
        Assert.All(open, route => Assert.True(
            route is "/dev/token" or "/health/live" or "/health/ready"
            || route.StartsWith("/openapi/", StringComparison.Ordinal)
            || route.StartsWith("/scalar", StringComparison.Ordinal),
            $"{route} is open to everyone."));
    }

    [Fact]
    public void The_development_issuer_and_audience_are_what_the_tokens_say()
    {
        Assert.Equal("cart-api-development", DevTokenIssuer.Issuer);
        Assert.Equal("cart-api", DevTokenIssuer.Audience);
    }

    private static string[] OpenRoutes(CartApiFactory factory) =>
        [.. factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Distinct()
            .Order(StringComparer.Ordinal)];

    private static async Task<HttpStatusCode> StatusWithTokenAsync(CartApiFactory factory, string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync(CartPath, Cancellation)).StatusCode;
    }
}
