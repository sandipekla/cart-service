namespace Carts.Domain;

/// <summary>
/// What a SKU looks like, defined once: 1 to 64 ASCII letters, digits, dots, underscores or
/// hyphens, starting with a letter or a digit.
/// </summary>
public static class SkuFormat
{
    public static bool IsValid(string? sku)
    {
        if (string.IsNullOrEmpty(sku) || sku.Length > CartLimits.MaxSkuLength || !char.IsAsciiLetterOrDigit(sku[0]))
        {
            return false;
        }

        return sku.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }
}
