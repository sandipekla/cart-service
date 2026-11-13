using System.Security.Claims;
using Carts.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Carts.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Bearer tokens. In Development they come from this API's own token endpoint; in every
    /// other environment they come from an OpenID Connect provider, and the API does not
    /// start without one. Each endpoint states whether it needs a token; a test fails for an
    /// endpoint that does not say.
    /// </summary>
    public static IServiceCollection AddCartAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization();

        var jwtBearer = services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme);

        if (environment.IsDevelopment())
        {
            services.AddSingleton<DevTokenIssuer>();
            jwtBearer.Configure<DevTokenIssuer>((options, issuer) =>
            {
                ApplyCommonRules(options);
                options.TokenValidationParameters.ValidIssuer = DevTokenIssuer.Issuer;
                options.TokenValidationParameters.ValidAudience = DevTokenIssuer.Audience;
                options.TokenValidationParameters.IssuerSigningKey = issuer.SigningKey;
                options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
            });
        }
        else
        {
            services.AddOptions<AuthOptions>()
                .Bind(configuration.GetSection(AuthOptions.SectionName))
                .Validate(
                    auth => auth.Authority == auth.Authority?.Trim()
                        && Uri.TryCreate(auth.Authority, UriKind.Absolute, out var authority)
                        && authority.Scheme == Uri.UriSchemeHttps,
                    "Auth:Authority must be the https address of the identity provider.")
                .Validate(
                    auth => !string.IsNullOrWhiteSpace(auth.Audience),
                    "Auth:Audience must name the audience of this API.")
                .ValidateOnStart();

            jwtBearer.Configure<IOptions<AuthOptions>>((options, auth) =>
            {
                ApplyCommonRules(options);
                options.Authority = auth.Value.Authority;
                options.Audience = auth.Value.Audience;

                // Only asymmetric signatures: a shared secret can never be a production credential.
                options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256];
            });
        }

        return services;
    }

    private static void ApplyCommonRules(JwtBearerOptions options)
    {
        // The framework also fills these options from the "Authentication:Schemes:Bearer"
        // configuration section. Everything it could have set there is replaced here, so a
        // configuration value can never add an issuer, an audience, a signing key or a plain
        // http key address to what this method allows.
        options.Authority = null;
        options.Audience = null;
        options.MetadataAddress = null!;
        options.Configuration = null;
        options.ConfigurationManager = null;
        options.RequireHttpsMetadata = true;

        // Keep the claim names as they are in the token ("sub" stays "sub").
        options.MapInboundClaims = false;

        // The answer to a bad token is 401 and nothing more: no hint about what was wrong.
        options.IncludeErrorDetails = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };

        options.Events = new JwtBearerEvents { OnTokenValidated = RejectTokenWithoutUsableSubject };
    }

    /// <summary>The cart belongs to the token's subject, so a token without one is of no use here.</summary>
    private static Task RejectTokenWithoutUsableSubject(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > CartLimits.MaxUserIdLength
            || subject.Any(char.IsControl))
        {
            context.Fail("The token has no usable subject.");
        }

        return Task.CompletedTask;
    }
}
