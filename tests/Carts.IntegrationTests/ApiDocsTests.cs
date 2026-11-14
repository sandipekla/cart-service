using System.Net;
using System.Text.Json;

namespace Carts.IntegrationTests;

public class ApiDocsTests(PostgresFixture postgres)
{
    private static readonly string[] DocumentedForSet = ["200", "400", "401", "404", "409", "422"];
    private static readonly string[] DocumentedForRemove = ["200", "400", "401", "409"];
    private static readonly string[] DocumentedForClear = ["200", "401", "409"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task In_development_the_documentation_and_the_health_endpoints_need_no_token(string path)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task The_api_description_lists_the_cart_and_says_it_needs_a_bearer_token()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Cancellation));

        var root = document.RootElement;
        Assert.True(root.GetProperty("paths").GetProperty("/api/v1/cart").TryGetProperty("get", out _));
        Assert.True(root.GetProperty("paths").GetProperty("/dev/token").TryGetProperty("post", out _));
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.True(root.GetProperty("paths").GetProperty("/api/v1/cart").GetProperty("get").TryGetProperty("security", out var security));
        Assert.True(security.GetArrayLength() > 0);
        Assert.False(root.GetProperty("paths").GetProperty("/dev/token").GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task The_api_description_types_amounts_as_numbers_and_lists_the_refusal()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Cancellation));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal("number", schemas.GetProperty("CartResponse").GetProperty("properties").GetProperty("total").GetProperty("type").GetString());
        Assert.Equal("number", schemas.GetProperty("CartItemResponse").GetProperty("properties").GetProperty("unitPrice").GetProperty("type").GetString());
        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/v1/cart").GetProperty("get").GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("401", out _));
    }

    [Fact]
    public async Task The_api_description_covers_the_routes_that_change_a_cart()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Cancellation));

        var paths = document.RootElement.GetProperty("paths");
        var item = paths.GetProperty("/api/v1/cart/items/{sku}");
        var put = item.GetProperty("put").GetProperty("responses");
        Assert.All(DocumentedForSet, status => Assert.True(put.TryGetProperty(status, out _), $"PUT does not document {status}."));
        var delete = item.GetProperty("delete").GetProperty("responses");
        Assert.All(DocumentedForRemove, status => Assert.True(delete.TryGetProperty(status, out _), $"DELETE item does not document {status}."));
        var clear = paths.GetProperty("/api/v1/cart").GetProperty("delete").GetProperty("responses");
        Assert.All(DocumentedForClear, status => Assert.True(clear.TryGetProperty(status, out _), $"DELETE cart does not document {status}."));
        Assert.True(item.GetProperty("put").TryGetProperty("security", out _));

        var quantity = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SetItemRequest").GetProperty("properties").GetProperty("quantity");
        Assert.Equal(1, quantity.GetProperty("minimum").GetInt32());
        Assert.Equal(99, quantity.GetProperty("maximum").GetInt32());
    }
}
