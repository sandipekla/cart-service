using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carts.IntegrationTests;

/// <summary>
/// The real API in memory, in an environment of the test's choice. A null connection string
/// leaves the setting alone, so the application's own configuration decides.
/// </summary>
public sealed class CartApiFactory(string? connectionString, string environment = "Development")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Carts", connectionString);
        }
    }
}
