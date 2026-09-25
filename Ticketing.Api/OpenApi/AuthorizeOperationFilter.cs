using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ticketing.Api.OpenApi;

/// <summary>
/// Marks operations that need a token (everything not [AllowAnonymous]) so Swagger UI sends it,
/// and documents the roles each one requires.
/// </summary>
internal sealed class AuthorizeOperationFilter : IOperationFilter
{
    public const string SchemeName = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            }
        );

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
