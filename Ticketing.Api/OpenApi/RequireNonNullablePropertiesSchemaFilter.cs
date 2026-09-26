using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ticketing.Api.OpenApi;

/// <summary>
/// Marks every non-nullable property as required, so the document says what the C# types
/// already guarantee. Without this, generated clients see every field as optional
/// (<c>name?: string</c>) even though the API always sends it.
/// </summary>
internal sealed class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is null)
        {
            return;
        }

        foreach (var (name, property) in concrete.Properties)
        {
            var nullable = property.Type?.HasFlag(JsonSchemaType.Null) == true;
            if (!nullable)
            {
                concrete.Required ??= new HashSet<string>();
                concrete.Required.Add(name);
            }
        }
    }
}
