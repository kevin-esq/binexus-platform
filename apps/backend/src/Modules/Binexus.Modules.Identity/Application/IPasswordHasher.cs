namespace Binexus.Modules.Identity.Application;

public interface IPasswordHasher
{
    /// <summary>UTF-8 password byte length inclusive bounds before Argon2.</summary>
    const int MinPasswordUtf8Bytes = 1;

    const int MaxPasswordUtf8Bytes = 1024;

    Task<string> HashAsync(string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string passwordHash, string password, CancellationToken cancellationToken = default);

    bool NeedsRehash(string passwordHash);
}
