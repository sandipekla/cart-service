using Carts.Domain;

namespace Carts.Api.Features.Carts;

/// <summary>Why a change to a cart was refused. Expected outcomes, not exceptions.</summary>
public enum CartFailure
{
    ProductNotFound,
    ProductUnavailable,
    CurrencyMismatch,
    ItemLimitExceeded,
}

/// <summary>
/// What a change to a cart led to: the cart as it now is (null when the user has none), or
/// the reason it was refused.
/// </summary>
public readonly record struct CartOutcome(Cart? Cart, CartFailure? Failure)
{
    public static CartOutcome Of(Cart? cart) => new(cart, null);

    public static CartOutcome Refused(CartFailure failure) => new(null, failure);
}
