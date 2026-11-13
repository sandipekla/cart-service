using System.Net;
using Carts.Api.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carts.IntegrationTests;

/// <summary>
/// The guard is the runtime half of "every endpoint says whether it needs a token": if one
/// ever slips in without saying, it fails instead of answering. The real API has no such
/// endpoint, so this is a tiny host with one, running the same security steps the API runs.
/// </summary>
public class EndpointAccessGuardTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_endpoint_that_does_not_say_whether_it_needs_a_token_never_answers()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/undeclared", Cancellation));
    }

    [Fact]
    public async Task Endpoints_that_do_say_are_left_alone()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/open", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Unknown_addresses_and_wrong_methods_keep_their_own_answers()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/nowhere", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/open", content: null, Cancellation)).StatusCode);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseCartAuth();
        app.MapGet("/undeclared", () => "should never be reached");
        app.MapGet("/open", () => "open").AllowAnonymous();
        app.MapGet("/protected", () => "protected").RequireAuthorization();

        await app.StartAsync(Cancellation);
        return app;
    }
}
