namespace Carts.Api.Auth;

/// <summary>The identity provider that issues the tokens this API accepts. Not used in Development.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Base address of the OpenID Connect provider; its signing keys are fetched from there.</summary>
    public string? Authority { get; set; }

    /// <summary>The audience a token must name to be meant for this API.</summary>
    public string? Audience { get; set; }
}
