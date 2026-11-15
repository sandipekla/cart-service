using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Carts.IntegrationTests;

/// <summary>
/// The path every environment but Development takes: tokens from an OpenID Connect provider.
/// The provider here is a stand-in that answers the two documents the API fetches, its
/// description and its public keys, so the real validation code runs against real signatures.
/// </summary>
public sealed class IdentityProviderTests(PostgresFixture postgres) : IDisposable
{
    private const string Authority = CartApiFactory.SomeAuthority;
    private const string CartPath = "/api/v1/cart";

    private readonly RSA _providerKey = RSA.Create(2048);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _providerKey.Dispose();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task A_token_the_provider_signed_for_this_api_is_accepted(string environment)
    {
        await using var factory = CreateFactory(environment);

        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(factory, ProviderToken(factory, subject: TestData.NewUserId())));
    }

    [Fact]
    public async Task A_token_the_provider_signed_for_another_api_is_refused()
    {
        await using var factory = CreateFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, audience: "another-api")));
    }

    [Fact]
    public async Task A_token_that_claims_another_issuer_is_refused()
    {
        await using var factory = CreateFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, issuer: "https://someone-else.invalid")));
    }

    [Fact]
    public async Task An_expired_token_from_the_provider_is_refused()
    {
        await using var factory = CreateFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, expiresIn: TimeSpan.FromMinutes(-5))));
    }

    [Fact]
    public async Task A_token_signed_by_a_key_the_provider_does_not_publish_is_refused()
    {
        await using var factory = CreateFactory();
        using var strangersKey = RSA.Create(2048);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, signedWith: strangersKey)));
    }

    [Fact]
    public async Task A_shared_secret_token_is_refused_even_when_the_secret_is_the_providers_public_key()
    {
        // The classic algorithm-confusion attack: the public key is public, so anyone can use it as an HMAC secret.
        await using var factory = CreateFactory();
        var publicKeyAsSecret = new SymmetricSecurityKey(_providerKey.ExportSubjectPublicKeyInfo());
        var forged = TestTokens.Create(
            factory,
            issuer: Authority,
            audience: "cart-api",
            credentials: new SigningCredentials(publicKeyAsSecret, SecurityAlgorithms.HmacSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, forged));
    }

    [Fact]
    public async Task A_token_the_provider_signed_with_an_algorithm_outside_the_allowed_list_is_refused()
    {
        // The key and the signature are genuine; only RS256 and ES256 are accepted.
        await using var factory = CreateFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, subject: TestData.NewUserId(), algorithm: SecurityAlgorithms.RsaSha512)));
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:ValidAudiences:0", "another-api")]
    [InlineData("Authentication:Schemes:Bearer:ValidAudience", "another-api")]
    public async Task Configuration_cannot_widen_the_audience_outside_development_either(string key, string value)
    {
        await using var factory = CreateFactory(settings: new Dictionary<string, string?> { [key] = value });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWithTokenAsync(factory, ProviderToken(factory, audience: "another-api")));
    }

    [Fact]
    public async Task Configuration_cannot_point_the_api_at_a_plain_http_key_address()
    {
        await using var factory = CreateFactory(settings: new Dictionary<string, string?>
        {
            ["Authentication:Schemes:Bearer:RequireHttpsMetadata"] = "false",
            ["Authentication:Schemes:Bearer:MetadataAddress"] = "http://attacker.invalid/.well-known/openid-configuration",
        });

        // The stand-in answers any address, so a 200 here would mean the http address was used.
        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(factory, ProviderToken(factory, subject: TestData.NewUserId())));
        Assert.DoesNotContain(_requestedAddresses, address => address.StartsWith("http://", StringComparison.Ordinal));
    }

    private readonly List<string> _requestedAddresses = [];

    private CartApiFactory CreateFactory(string environment = "Production", IReadOnlyDictionary<string, string?>? settings = null) =>
        new(
            postgres.ConnectionString,
            environment,
            authority: Authority,
            audience: "cart-api",
            settings: settings,
            identityProvider: new StandInProvider(_providerKey, _requestedAddresses));

    private string ProviderToken(
        CartApiFactory factory,
        string? subject = "alice",
        string issuer = Authority,
        string audience = "cart-api",
        TimeSpan? expiresIn = null,
        RSA? signedWith = null,
        string algorithm = SecurityAlgorithms.RsaSha256) =>
        TestTokens.Create(
            factory,
            subject,
            issuer,
            audience,
            expiresIn,
            credentials: new SigningCredentials(new RsaSecurityKey(signedWith ?? _providerKey) { KeyId = signedWith is null ? StandInProvider.KeyId : "stranger" }, algorithm));

    private static async Task<HttpStatusCode> StatusWithTokenAsync(CartApiFactory factory, string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync(CartPath, Cancellation)).StatusCode;
    }

    private sealed class StandInProvider(RSA key, List<string> requestedAddresses) : HttpMessageHandler
    {
        public const string KeyId = "provider-key-1";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.ToString();
            lock (requestedAddresses)
            {
                requestedAddresses.Add(address);
            }

            var json = address.EndsWith("/keys", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { keys = new[] { PublicKey() } })
                : JsonSerializer.Serialize(new { issuer = Authority, jwks_uri = $"{Authority}/keys" });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        private object PublicKey()
        {
            var parameters = key.ExportParameters(includePrivateParameters: false);
            return new
            {
                kty = "RSA",
                use = "sig",
                kid = KeyId,
                alg = "RS256",
                n = Base64UrlEncoder.Encode(parameters.Modulus),
                e = Base64UrlEncoder.Encode(parameters.Exponent),
            };
        }
    }
}
