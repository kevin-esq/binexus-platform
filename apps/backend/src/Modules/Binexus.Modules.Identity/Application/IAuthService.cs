namespace Binexus.Modules.Identity.Application;

public interface IAuthService
{
    Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<AuthSession> GetSessionAsync(Guid userId, CancellationToken cancellationToken = default);
}
