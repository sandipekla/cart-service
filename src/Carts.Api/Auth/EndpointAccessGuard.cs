using Microsoft.AspNetCore.Authorization;

namespace Carts.Api.Auth;

internal static class EndpointAccessGuard
{
    /// <summary>
    /// Every endpoint must say whether it needs a token. Tests check that at build time; this
    /// is the same rule at run time: an endpoint that says neither fails instead of answering.
    /// It only sees mapped endpoints, so anything added as plain middleware is outside it.
    /// </summary>
    public static void UseEndpointAccessGuard(this WebApplication app) =>
        app.Use((context, next) =>
        {
            if (context.GetEndpoint() is RouteEndpoint endpoint
                && endpoint.Metadata.GetMetadata<IAuthorizeData>() is null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                throw new InvalidOperationException(
                    $"The endpoint '{endpoint.DisplayName}' does not say whether it needs a token.");
            }

            return next(context);
        });
}
