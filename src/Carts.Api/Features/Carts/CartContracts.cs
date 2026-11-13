using System.ComponentModel.DataAnnotations;
using Carts.Domain;

namespace Carts.Api.Features.Carts;

/// <summary>A cart as the API shows it. A user without a cart gets the same shape, empty.</summary>
public sealed record CartResponse(
    Guid? Id,
    int Version,
    string? Currency,
    IReadOnlyList<CartItemResponse> Items,
    decimal Total,
    DateTimeOffset? UpdatedAt)
{
    private static readonly CartResponse Empty = new(null, 0, null, [], 0m, null);

    public static CartResponse From(Cart? cart) =>
        cart is null
            ? Empty
            : new CartResponse(
                cart.Id,
                cart.Version,
                cart.Currency,
                [.. cart.Items
                    .OrderBy(item => item.AddedAt)
                    .ThenBy(item => item.Sku, StringComparer.Ordinal)
                    .Select(item => new CartItemResponse(item.Sku, item.ProductName, item.UnitPrice, item.Quantity, item.LineTotal))],
                cart.Total,
                cart.UpdatedAt);
}

public sealed record CartItemResponse(string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

/// <summary>
/// The body of "set this item". It carries a quantity and nothing else: the product's name
/// and price always come from the service's own data, never from the caller.
/// </summary>
public sealed record SetItemRequest(
    [property: Range(1, CartLimits.MaxQuantity)] int? Quantity);
