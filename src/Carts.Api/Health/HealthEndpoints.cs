using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Carts.Api.Health;

public static class HealthEndpoints
{
    /// <summary>Marks the checks that decide readiness.</summary>
    public const string ReadyTag = "ready";

    public static void MapHealthEndpoints(this WebApplication app)
    {
        // Liveness runs no checks: it only says the process responds. A failing dependency
        // must never get the container restarted.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        // Readiness asks the database. Failing means "stop sending me traffic", not "restart me".
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));
    }
}
