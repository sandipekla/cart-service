using Carts.Api.Auth;
using Carts.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Carts.Api.Features.Carts;

public static class CartEndpoints
{
    private const string BasePath = "/api/v1/cart";

    /// <summary>
    /// A cart is one person's data, so no cache may keep any answer from a cart address. This
    /// runs before authentication and before the request body is read, so it also covers the
    /// answers the framework gives on the API's behalf: 401, 405, 415 and an unreadable body.
    /// </summary>
    public static void UseNoStoreForCartAnswers(this WebApplication app) =>
        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(BasePath))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            return next(context);
        });

    /// <summary>
    /// The cart has no id in its address: it is always the caller's own, so another user's
    /// cart cannot even be asked for. Every route answers with the cart as it now is.
    /// </summary>
    public static void MapCartEndpoints(this WebApplication app)
    {
        var cart = app.MapGroup(BasePath)
            .WithTags("Cart")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        cart.MapGet(string.Empty, GetCart)
            .WithSummary("The caller's cart. A user without a cart gets the empty cart.");

        cart.MapPut("items/{sku}", SetItem)
            .WithSummary("Set the quantity of a product. It replaces the current quantity; it never adds to it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cart.MapDelete("items/{sku}", RemoveItem)
            .WithSummary("Remove a product. Removing one that is not in the cart changes nothing.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        cart.MapDelete(string.Empty, ClearCart)
            .WithSummary("Remove every product. The cart itself stays.")
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<CartResponse>> GetCart(
        CurrentUser user,
        CartService carts,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(CartResponse.From(await carts.FindAsync(user.Id, cancellationToken)));

    private static async Task<Results<Ok<CartResponse>, ValidationProblem, ProblemHttpResult>> SetItem(
        string sku,
        SetItemRequest request,
        CurrentUser user,
        CartService carts,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        // Both mistakes are reported together, and before the database is asked anything.
        var errors = new Dictionary<string, string[]>();
        if (!SkuFormat.IsValid(sku))
        {
            errors["sku"] = [CartProblems.SkuRule];
        }

        // A missing quantity is treated as zero, which the range refuses.
        var quantity = request.Quantity ?? 0;
        if (quantity is < 1 or > CartLimits.MaxQuantity)
        {
            errors["quantity"] = [CartProblems.QuantityRule];
        }

        if (errors.Count > 0)
        {
            return CartProblems.Validation(errors);
        }

        var outcome = await carts.SetItemAsync(user.Id, sku, quantity, cancellationToken);

        return outcome.Failure is { } failure
            ? Refuse(failure, response)
            : TypedResults.Ok(CartResponse.From(outcome.Cart));
    }

    private static async Task<Results<Ok<CartResponse>, ValidationProblem, ProblemHttpResult>> RemoveItem(
        string sku,
        CurrentUser user,
        CartService carts,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!SkuFormat.IsValid(sku))
        {
            return CartProblems.Validation(new Dictionary<string, string[]> { ["sku"] = [CartProblems.SkuRule] });
        }

        var outcome = await carts.RemoveItemAsync(user.Id, sku, cancellationToken);

        return outcome.Failure is { } failure
            ? Refuse(failure, response)
            : TypedResults.Ok(CartResponse.From(outcome.Cart));
    }

    private static async Task<Results<Ok<CartResponse>, ProblemHttpResult>> ClearCart(
        CurrentUser user,
        CartService carts,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var outcome = await carts.ClearAsync(user.Id, cancellationToken);

        return outcome.Failure is { } failure
            ? Refuse(failure, response)
            : TypedResults.Ok(CartResponse.From(outcome.Cart));
    }

    /// <summary>A busy cart also says when trying again makes sense; a problem document has no place for a header.</summary>
    private static ProblemHttpResult Refuse(CartFailure failure, HttpResponse response)
    {
        if (failure is CartFailure.CartBusy)
        {
            response.Headers.RetryAfter = CartProblems.RetryAfterSeconds;
        }

        return CartProblems.For(failure);
    }
}
