using Binexus.Platform.Branching.DeviceAuth;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Binexus.Api.OpenApi;

/// <summary>
/// Declares UserBearer + DeviceBearer. Device-auth /me uses DeviceBearer alone.
/// Branch operational module paths use AND composition (single requirement with both schemes).
/// </summary>
internal sealed class BranchDeviceAuthOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private static readonly string[] OperationalPathPrefixes =
    [
        "/sales/",
        "/inventory/",
        "/orders/",
        "/warehouse/",
        "/logistics/",
    ];

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes["UserBearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "Interim user access token (Authorization: Bearer). On Branch operational APIs, compose with DeviceBearer (AND).",
        };

        document.Components.SecuritySchemes["DeviceBearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            Description =
                "Device Access Token. Value form: Bearer <DAT>. Never place DAT material in OpenAPI examples. Compose with UserBearer (AND) for Branch operational modules.",
        };

        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var path in document.Paths)
        {
            if (path.Value?.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Value.Operations)
            {
                if (operation.Value is null)
                {
                    continue;
                }

                if (path.Key.Equals("/branch/device-auth/me", StringComparison.OrdinalIgnoreCase)
                    && operation.Key == HttpMethod.Get)
                {
                    operation.Value.Security =
                    [
                        new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("DeviceBearer", document)] = [],
                        },
                    ];
                    continue;
                }

                if (IsOperationalPath(path.Key))
                {
                    // Single requirement object with two schemes = AND (not OR alternatives).
                    operation.Value.Security =
                    [
                        new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("UserBearer", document)] = [],
                            [new OpenApiSecuritySchemeReference("DeviceBearer", document)] = [],
                        },
                    ];
                }
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsOperationalPath(string path) =>
        OperationalPathPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
