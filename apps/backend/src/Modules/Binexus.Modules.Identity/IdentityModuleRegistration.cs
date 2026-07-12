using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Features.Auth;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.Modules.Identity;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var environmentName = environment?.EnvironmentName
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environments.Production;
        var isTesting = string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        var isDevelopment = string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
        var isProductionLike = !isTesting && !isDevelopment;

        RejectKnownDemoPasswordInProduction(configuration, isProductionLike);
        RejectKnownInsecureSigningKeyInProduction(configuration, isProductionLike);

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            if (isTesting || IsOpenApiDocumentGenerationHost())
            {
                jwtOptions.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            }
            else
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey must be configured via environment variable or user-secrets (minimum 32 UTF-8 bytes).");
            }
        }

        if ((isDevelopment || isTesting)
            && string.Equals(
                jwtOptions.SigningKey,
                IdentitySeedDefaults.KnownInsecureLocalSigningKey,
                StringComparison.Ordinal))
        {
            Debug.WriteLine(
                "Jwt:SigningKey is using the DEVELOPMENT-ONLY .env.example placeholder. Never use this value in Staging or Production.");
        }
        if (IsOpenApiDocumentGenerationHost() || isTesting)
        {
            if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
            {
                jwtOptions.Issuer = "binexus";
            }

            if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
            {
                jwtOptions.Audience = "binexus-api";
            }

            if (jwtOptions.AccessTokenLifetime <= TimeSpan.Zero)
            {
                jwtOptions.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            }

            if (jwtOptions.RefreshTokenLifetime <= TimeSpan.Zero)
            {
                jwtOptions.RefreshTokenLifetime = TimeSpan.FromDays(7);
            }

            if (jwtOptions.ClockSkew < TimeSpan.Zero)
            {
                jwtOptions.ClockSkew = TimeSpan.FromSeconds(30);
            }
        }

        jwtOptions.Validate();
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        services.AddSingleton(jwtOptions);
        services.Configure<IdentitySeedOptions>(configuration.GetSection(IdentitySeedOptions.SectionName));
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<JwtTokenIssuer>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantFeatureService, TenantFeatureService>();
        services.AddSingleton<IDbContextModelContributor, IdentityDbContextModelContributor>();

        if (isDevelopment || isTesting)
        {
            services.AddScoped<DevelopmentIdentitySeeder>();
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = jwtOptions.ClockSkew,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role",
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal!;
                        if (!TryGetGuid(principal, "tenantId", out var tenantId)
                            || !TryGetGuid(principal, JwtRegisteredClaimNames.Sub, out var userId))
                        {
                            context.Fail("Required identity claims are missing.");
                            return Task.CompletedTask;
                        }

                        Guid? branchId = null;
                        if (TryGetGuid(principal, "branchId", out var parsedBranchId))
                        {
                            branchId = parsedBranchId;
                        }

                        var currentTenant = context.HttpContext.RequestServices
                            .GetRequiredService<ICurrentTenant>();
                        currentTenant.SetContext(new TenantContext(
                            tenantId,
                            userId,
                            principal.FindFirstValue("role"),
                            branchId,
                            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        var code = context.AuthenticateFailure is SecurityTokenExpiredException
                            ? AuthErrorCodes.TokenExpired
                            : AuthErrorCodes.Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            type = $"https://binexus.dev/errors/{code}",
                            title = code,
                            status = StatusCodes.Status401Unauthorized,
                            detail = code == AuthErrorCodes.TokenExpired
                                ? "Access token expired."
                                : "Authentication required.",
                            code,
                        });
                    },
                };
            });
        services.AddAuthorization();

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints) =>
        AuthEndpoints.MapIdentityEndpoints(endpoints);

    private static void RejectKnownDemoPasswordInProduction(
        IConfiguration configuration,
        bool isProductionLike)
    {
        if (!isProductionLike)
        {
            return;
        }

        var configured = configuration[$"{IdentitySeedOptions.SectionName}:AdminPassword"];
        if (string.Equals(
                configured,
                IdentitySeedDefaults.KnownInsecureDemoPassword,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "IdentitySeed:AdminPassword must not use the known insecure demo password placeholder in Production or Staging.");
        }
    }

    private static void RejectKnownInsecureSigningKeyInProduction(
        IConfiguration configuration,
        bool isProductionLike)
    {
        if (!isProductionLike)
        {
            return;
        }

        var signingKey = configuration[$"{JwtOptions.SectionName}:SigningKey"];
        if (string.Equals(
                signingKey,
                IdentitySeedDefaults.KnownInsecureLocalSigningKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must not use the DEVELOPMENT-ONLY .env.example placeholder in Production or Staging.");
        }
    }

    private static bool TryGetGuid(ClaimsPrincipal principal, string claimType, out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value);

    /// <summary>
    /// OpenAPI document generation hosts the app without secrets; allow an ephemeral key only then.
    /// </summary>
    private static bool IsOpenApiDocumentGenerationHost()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        return entry.Contains("GetDocument", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("dotnet-getdocument", StringComparison.OrdinalIgnoreCase);
    }
}
