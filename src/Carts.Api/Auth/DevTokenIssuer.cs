using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Carts.Api.Auth;

/// <summary>
/// Signs tokens for local use. The key is made up when the process starts and lives only in
/// memory, so there is no development secret to commit, configure or leak, and a token stops
/// working when the API restarts. Registered in the Development environment only.
/// </summary>
public sealed class DevTokenIssuer
{
    public const string Issuer = "cart-api-development";
    public const string Audience = "cart-api";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly SymmetricSecurityKey _key = new(RandomNumberGenerator.GetBytes(32));

    public SecurityKey SigningKey => _key;

    public string Issue(string userId, DateTimeOffset now) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId)]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + Lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
}
