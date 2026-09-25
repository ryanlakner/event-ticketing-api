namespace Ticketing.Api.OpenApi;

/// <summary>
/// Entra ID sign-in for Swagger UI (OAuth2 authorization code + PKCE). Configured per environment
/// by Terraform; when absent, Swagger UI offers only "paste a bearer token".
/// </summary>
public sealed class SwaggerSignInOptions
{
    public const string SectionName = "Swagger:SignIn";

    /// <summary>The Swagger UI app registration: a public SPA client, so no secret exists.</summary>
    public string? ClientId { get; set; }

    public Uri? AuthorizationUrl { get; set; }

    public Uri? TokenUrl { get; set; }

    /// <summary>The API scope to request, e.g. api://&lt;api client id&gt;/access_as_user.</summary>
    public string? Scope { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && AuthorizationUrl is not null
        && TokenUrl is not null
        && !string.IsNullOrWhiteSpace(Scope);
}
