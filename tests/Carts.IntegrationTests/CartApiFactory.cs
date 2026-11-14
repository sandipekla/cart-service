using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carts.IntegrationTests;

/// <summary>
/// The real API in memory, in an environment of the test's choice. A null connection string
/// leaves the setting alone, so the application's own configuration decides.
/// </summary>
public sealed class CartApiFactory(
    string? connectionString,
    string environment = "Development",
    string? authority = CartApiFactory.SomeAuthority,
    string? audience = "cart-api",
    IReadOnlyDictionary<string, string?>? settings = null,
    HttpMessageHandler? identityProvider = null,
    TimeProvider? time = null)
    : WebApplicationFactory<Program>
{
    /// <summary>An identity provider address that resolves nowhere. Enough to start; nothing is ever fetched from it successfully.</summary>
    public const string SomeAuthority = "https://identity.invalid";

    public CapturedLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Carts", connectionString);
        }

        // Development needs no identity provider; every other environment does.
        if (environment != "Development")
        {
            if (authority is not null)
            {
                builder.UseSetting("Auth:Authority", authority);
            }

            if (audience is not null)
            {
                builder.UseSetting("Auth:Audience", audience);
            }
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (identityProvider is not null)
        {
            // Calls to the identity provider go to the stand-in, not to the network.
            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = identityProvider));
        }

        if (time is not null)
        {
            // The clock is the one thing a test may swap inside the application.
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    /// <summary>A client that carries a development token for the given user.</summary>
    public async Task<HttpClient> CreateClientForAsync(string userId, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/dev/token", new { userId }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private sealed record TokenResponse(string AccessToken);
}
