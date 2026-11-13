using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Carts.Api.Auth;

/// <summary>
/// The caller, taken from the validated token and from nowhere else. An endpoint that asks
/// for a CurrentUser parameter gets it bound from the request; no URL, query or body value
/// can say who the caller is.
/// </summary>
public sealed record CurrentUser(string Id)
{
    public static ValueTask<CurrentUser?> BindAsync(HttpContext context)
    {
        // Token validation already refused a token without a subject, so this is never null
        // on an endpoint that requires authentication.
        var subject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return ValueTask.FromResult(subject is null ? null : new CurrentUser(subject));
    }
}
