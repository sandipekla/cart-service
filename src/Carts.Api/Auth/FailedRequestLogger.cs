using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Carts.Api.Auth;

/// <summary>
/// When a request fails with an unhandled exception, the framework logs the exception after
/// the request has left the user's log scope. This adds the one line that says whose request
/// it was; both lines share the trace id. It never handles the exception itself.
/// </summary>
internal sealed partial class FailedRequestLogger(ILogger<FailedRequestLogger> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var userId = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is not null)
        {
            LogFailedForUser(logger, userId);
        }

        return ValueTask.FromResult(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The request of user {UserId} failed with an unhandled exception.")]
    private static partial void LogFailedForUser(ILogger logger, string userId);
}
