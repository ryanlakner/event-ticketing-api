using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ticketing.Api.OpenApi;

/// <summary>
/// Marks operations that need a token (everything not [AllowAnonymous]) so Swagger UI sends it,
/// and documents the roles each one requires. Either scheme satisfies the requirement: a pasted
/// bearer token, or Entra ID sign-in when it is configured.
/// </summary>
internal sealed class AuthorizeOperationFilter(SwaggerSignInOptions signIn) : IOperationFilter
{
    public const string SchemeName = "Bearer";
    public const string SignInSchemeName = "EntraId";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        // Separate requirement objects are alternatives (OR) in OpenAPI.
        operation.Security ??= [];
        operation.Security.Add(
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            }
        );
        if (signIn.IsConfigured)
        {
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SignInSchemeName, context.Document)] =
                    [
                        signIn.Scope!,
                    ],
                }
            );
        }

        var roles = metadata
            .OfType<IAuthorizeData>()
            .SelectMany(a => a.Roles?.Split(',') ?? [])
            .Distinct()
            .ToList();
        if (roles.Count > 0)
        {
            operation.Description =
                $"{operation.Description}\n\nRequires role: **{string.Join("** or **", roles)}**.".Trim();
        }

        operation.Responses ??= [];
        operation.Responses.TryAdd(
            "401",
            new OpenApiResponse { Description = "Missing, expired, or invalid access token." }
        );
        operation.Responses.TryAdd(
            "403",
            new OpenApiResponse { Description = "Signed in, but not allowed to do this." }
        );
    }
}
