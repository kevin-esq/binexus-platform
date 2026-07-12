using System.IdentityModel.Tokens.Jwt;
using Binexus.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Identity.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting("auth")
            .Produces<AuthTokens>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting("auth")
            .Produces<AuthTokens>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", GetSessionAsync)
            .RequireAuthorization()
            .Produces<AuthSession>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantSlug)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthError(new AuthException(
                AuthErrorCodes.InvalidCredentials,
                "Invalid credentials."));
        }

        try
        {
            return Results.Ok(await authService.LoginAsync(request, cancellationToken));
        }
        catch (AuthException exception)
        {
            return AuthError(exception);
        }
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await authService.RefreshAsync(
                request.RefreshToken,
                cancellationToken));
        }
        catch (AuthException exception)
        {
            return AuthError(exception);
        }
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext context,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return AuthError(new AuthException(AuthErrorCodes.Forbidden, "Forbidden."));
        }

        try
        {
            return Results.Ok(await authService.GetSessionAsync(userId, cancellationToken));
        }
        catch (AuthException exception)
        {
            return AuthError(exception);
        }
    }

    private static IResult AuthError(AuthException exception)
    {
        var status = exception.Code switch
        {
            AuthErrorCodes.Forbidden or AuthErrorCodes.AccountUnavailable => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized,
        };
        return Results.Problem(
            detail: exception.Message,
            statusCode: status,
            title: exception.Code,
            type: $"https://binexus.dev/errors/{exception.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.Code,
            });
    }
}
