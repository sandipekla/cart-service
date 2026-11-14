using Carts.Domain;

namespace Carts.Api.Features.Carts;

/// <summary>Why a change to a cart was refused. Expected outcomes, not exceptions.</summary>
public enum CartFailure
{
    ProductNotFound,
    ProductUnavailable,
    CurrencyMismatch,
    ItemLimitExceeded,

    /// <summary>Another change to the same cart did not finish in time. Trying again is safe.</summary>
    CartBusy,

    /// <summary>Something changed the cart without taking the cart lock. Refusing is the safe answer; the cause is a bug and is logged as one.</summary>
    ConcurrencyConflict,
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
