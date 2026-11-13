using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Carts.Api.ApiDocs;

/// <summary>The API description and a page to read and try it. Development only.</summary>
public static class ApiDocsExtensions
{
    public static IServiceCollection AddCartApiDocs(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Cart API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            // Amounts are written by a converter of our own, so the description is told their type.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(decimal))
                {
                    schema.Type = JsonSchemaType.Number;
                    schema.Format = "decimal";
                }

                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecurityTransformer>();
            options.AddOperationTransformer<BearerSecurityTransformer>();
        });

    public static void MapCartApiDocs(this WebApplication app)
    {
        app.MapOpenApi().AllowAnonymous();
        // The page shows this API only: no vendor toolbar, no assistant, no fonts fetched from
        // another site, and the bearer token box already open.
        app.MapScalarApiReference(options => options
                .WithTitle("Cart API")
                .DisableDefaultFonts()
                .HideDeveloperTools()
                .DisableAgent()
                .AddPreferredSecuritySchemes("Bearer"))
            .AllowAnonymous();
    }
}
