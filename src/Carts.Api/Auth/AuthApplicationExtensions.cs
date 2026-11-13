using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Carts.Api.Auth;

public static partial class AuthApplicationExtensions
{
    /// <summary>
    /// The security steps of the request pipeline, in the one order that works: find out who
    /// is calling, label the logs with them, decide whether they may, and refuse to run an
    /// endpoint that never said whether it needs a token.
    /// </summary>
    public static void UseCartAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseUserLogScope();
        app.UseAuthorization();
        app.UseEndpointAccessGuard();
    }

    /// <summary>Everything logged while a request is handled carries the caller's id. Ids only: never a token.</summary>
    private static void UseUserLogScope(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId is null)
            {
                await next(context);
                return;
            }

            using (UserScope(app.Logger, userId))
            {
                await next(context);
            }
        });

    /// <summary>Development accepts tokens this API signs for anyone who asks. Say so where an operator will see it.</summary>
    public static void WarnAboutDevelopmentAuthentication(this WebApplication app) =>
        LogDevelopmentAuthentication(app.Logger);

    private static readonly Func<ILogger, string, IDisposable?> UserScope =
        LoggerMessage.DefineScope<string>("UserId:{UserId}");

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Development authentication is on: POST /dev/token issues a token for any user id. Never run this environment where others can reach it.")]
    private static partial void LogDevelopmentAuthentication(ILogger logger);
}
