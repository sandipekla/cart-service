using Carts.Api.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Carts.Api.Features.Carts;

public static class CartEndpoints
{
    /// <summary>
    /// The cart has no id in its address: it is always the caller's own, so another user's
    /// cart cannot even be asked for.
    /// </summary>
    public static void MapCartEndpoints(this WebApplication app)
    {
        var cart = app.MapGroup("/api/v1/cart")
            .WithTags("Cart")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        cart.MapGet(string.Empty, GetCart)
            .WithSummary("The caller's cart. A user without a cart gets the empty cart.");
    }

    private static async Task<Ok<CartResponse>> GetCart(
        CurrentUser user,
        CartService carts,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        // A cart is one person's data; no cache may keep a copy.
        response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(CartResponse.From(await carts.FindAsync(user.Id, cancellationToken)));
    }
}
