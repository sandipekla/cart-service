using Microsoft.AspNetCore.Http.HttpResults;

namespace Carts.Api.Auth;

/// <summary>Hands out a token for any user id, for trying the API locally. Mapped in Development only.</summary>
public static class DevTokenEndpoint
{
    private const int MaxDevUserIdLength = 64;

    public static void MapDevTokenEndpoint(this WebApplication app) =>
        app.MapPost("/dev/token", IssueToken)
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Issue a token for a user id of your choice. Development only.");

    private static Results<Ok<DevTokenResponse>, ValidationProblem> IssueToken(
        DevTokenRequest request,
        DevTokenIssuer issuer,
        TimeProvider time,
        HttpResponse response)
    {
        if (!IsValidUserId(request.UserId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = [$"Required: 1 to {MaxDevUserIdLength} letters, digits, dots, underscores or hyphens."],
            });
        }

        // The answer is a credential; no cache may keep it.
        response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(new DevTokenResponse(
            issuer.Issue(request.UserId, time.GetUtcNow()),
            "Bearer",
            (int)DevTokenIssuer.Lifetime.TotalSeconds));
    }

    private static bool IsValidUserId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? userId) =>
        !string.IsNullOrEmpty(userId)
        && userId.Length <= MaxDevUserIdLength
        && userId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}

public sealed record DevTokenRequest(string? UserId);

public sealed record DevTokenResponse(string AccessToken, string TokenType, int ExpiresIn);
