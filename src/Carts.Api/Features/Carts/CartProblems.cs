using Carts.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Carts.Api.Features.Carts;

/// <summary>
/// The cart's own errors as problem details. Each carries a stable "code", which is what a
/// client should switch on; titles and details are for people.
/// </summary>
internal static class CartProblems
{
    private const string CodeKey = "code";
    private const string ValidationFailed = "validation_failed";

    public static readonly string SkuRule =
        $"1 to {CartLimits.MaxSkuLength} letters, digits, dots, underscores or hyphens, starting with a letter or a digit.";

    public static readonly string QuantityRule =
        $"Required: a whole number from 1 to {CartLimits.MaxQuantity}.";

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, extensions: Code(ValidationFailed));

    public static ProblemHttpResult For(CartFailure failure) => failure switch
    {
        CartFailure.ProductNotFound => Problem(
            StatusCodes.Status404NotFound,
            "product_not_found",
            "Product not found",
            "No product has this SKU."),
        CartFailure.ProductUnavailable => Problem(
            StatusCodes.Status422UnprocessableEntity,
            "product_unavailable",
            "Product unavailable",
            "This product is no longer sold. It can be removed from a cart, but not added or changed."),
        CartFailure.CurrencyMismatch => Problem(
            StatusCodes.Status422UnprocessableEntity,
            "currency_mismatch",
            "Currency mismatch",
            "A cart holds products of one currency. This product is priced in another."),
        CartFailure.ItemLimitExceeded => Problem(
            StatusCodes.Status422UnprocessableEntity,
            "item_limit_exceeded",
            "Too many different products",
            $"A cart holds at most {CartLimits.MaxItems} different products."),
        _ => throw new InvalidOperationException($"No problem is defined for {failure}."),
    };

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(detail, statusCode: status, title: title, extensions: Code(code));

    private static Dictionary<string, object?> Code(string code) => new() { [CodeKey] = code };
}
