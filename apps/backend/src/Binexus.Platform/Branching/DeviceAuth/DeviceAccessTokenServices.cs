using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Binexus.Platform.Branching.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.Platform.Branching.DeviceAuth;

public interface IDeviceAccessTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAtUtc) Issue(DeviceAccessTokenSubject subject, DateTimeOffset now);
}

public interface IDeviceAccessTokenValidator
{
    ClaimsPrincipal Validate(string token, DateTimeOffset now);
}

public sealed record DeviceAccessTokenSubject(
    Guid DeviceId,
    Guid BranchInstanceId,
    Guid TenantId,
    Guid BranchId,
    Guid TerminalId,
    string SecurityStamp);

public sealed class DeviceAccessTokenIssuer(IOptions<BranchDeviceAuthOptions> options) : IDeviceAccessTokenIssuer
{
    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(DeviceAccessTokenSubject subject, DateTimeOffset now)
    {
        var opts = options.Value;
        var key = opts.SigningKeys.Single(k => k.KeyId == opts.CurrentKeyId);
        var expires = now.AddSeconds(opts.TokenLifetimeSeconds);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key.Key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.DeviceId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, Epoch(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new("branch_instance_id", subject.BranchInstanceId.ToString("D")),
            new("tenant_id", subject.TenantId.ToString("D")),
            new("branch_id", subject.BranchId.ToString("D")),
            new("terminal_id", subject.TerminalId.ToString("D")),
            new("device_security_stamp", subject.SecurityStamp),
            new("token_type", DeviceAuthCryptoFormats.TokenType),
            new("ver", "1"),
        };

        var token = new JwtSecurityToken(
            issuer: $"binexus-branch-device/{subject.BranchInstanceId:D}",
            audience: DeviceAuthCryptoFormats.TokenAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        token.Header["kid"] = opts.CurrentKeyId;

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static long Epoch(DateTimeOffset value) => value.ToUnixTimeSeconds();
}

public sealed class DeviceAccessTokenValidator(IOptions<BranchDeviceAuthOptions> options) : IDeviceAccessTokenValidator
{
    public ClaimsPrincipal Validate(string token, DateTimeOffset now)
    {
        var opts = options.Value;
        var keys = opts.SigningKeys.ToDictionary(
            k => k.KeyId,
            k => (SecurityKey)new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k.Key)),
            StringComparer.Ordinal);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = null,
                IssuerValidator = (issuer, _, _) =>
                {
                    if (issuer is null
                        || !issuer.StartsWith("binexus-branch-device/", StringComparison.Ordinal))
                    {
                        throw new SecurityTokenInvalidIssuerException("Invalid DAT issuer.");
                    }

                    return issuer;
                },
                ValidateAudience = true,
                ValidAudience = DeviceAuthCryptoFormats.TokenAudience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(opts.ClockSkewSeconds),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (_, securityToken, kid, _) =>
                {
                    if (securityToken is not JwtSecurityToken jwt)
                    {
                        return [];
                    }

                    var keyId = kid ?? jwt.Header.Kid;
                    if (keyId is null || !keys.TryGetValue(keyId, out var key))
                    {
                        return [];
                    }

                    return [key];
                },
            }, out var validated);

            if (validated is not JwtSecurityToken jwtToken
                || !string.Equals(
                    jwtToken.Claims.FirstOrDefault(c => c.Type == "token_type")?.Value,
                    DeviceAuthCryptoFormats.TokenType,
                    StringComparison.Ordinal))
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceTokenInvalid, "Invalid device token.");
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceTokenExpired, "Device token expired.");
        }
        catch (DeviceAuthException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceTokenInvalid, "Invalid device token.");
        }
    }
}
