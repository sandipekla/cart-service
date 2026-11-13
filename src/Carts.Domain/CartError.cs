namespace Carts.Domain;

/// <summary>Why a cart refused a change. An expected outcome, not an exception.</summary>
public enum CartError
{
    InvalidQuantity,
    ProductUnavailable,
    CurrencyMismatch,
    LineLimitExceeded,
}
