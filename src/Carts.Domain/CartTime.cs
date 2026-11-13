namespace Carts.Domain;

/// <summary>How a cart keeps time.</summary>
internal static class CartTime
{
    /// <summary>
    /// Every time a cart keeps is in UTC and cut to the microsecond, so a cart read back from
    /// storage compares equal to the one that was saved.
    /// </summary>
    public static DateTimeOffset Normalize(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
