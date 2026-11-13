using System.Security.Claims;
using System.Security.Cryptography;
using Carts.Api.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Carts.IntegrationTests;

/// <summary>Builds tokens that are wrong in exactly one way, to prove each check on its own.</summary>
internal static class TestTokens
{
    public static string Create(
        CartApiFactory factory,
        string? subject = "alice",
        string issuer = DevTokenIssuer.Issuer,
        string audience = DevTokenIssuer.Audience,
        TimeSpan? expiresIn = null,
        SecurityKey? signingKey = null,
        bool signed = true,
        bool withExpiry = true,
        SigningCredentials? credentials = null)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(10));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = subject is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim("sub", subject)]),
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = withExpiry ? expires : null,
        };

        if (credentials is not null)
        {
            descriptor.SigningCredentials = credentials;
        }
        else if (signed)
        {
            var key = signingKey ?? factory.Services.GetRequiredService<DevTokenIssuer>().SigningKey;
            descriptor.SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        // Without this the handler would quietly add an expiry of its own.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = withExpiry }.CreateToken(descriptor);
    }

    public static SecurityKey SomeOtherKey() => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
}
