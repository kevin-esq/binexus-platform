using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.DeviceAuth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class DeviceAuthSecurityMatrixTests
{
    private const string SigningKey = "integration-test-branch-device-auth-signing-key-32b";
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("binexus-branch-device/11111111-1111-1111-1111-111111111111", "binexus-branch-device", DeviceAuthCryptoFormats.TokenType, "test-dat-1", false, true)]
    [InlineData("wrong-issuer", "binexus-branch-device", DeviceAuthCryptoFormats.TokenType, "test-dat-1", false, false)]
    [InlineData("binexus-branch-device/11111111-1111-1111-1111-111111111111", "wrong-audience", DeviceAuthCryptoFormats.TokenType, "test-dat-1", false, false)]
    [InlineData("binexus-branch-device/11111111-1111-1111-1111-111111111111", "binexus-branch-device", "wrong-type", "test-dat-1", false, false)]
    [InlineData("binexus-branch-device/11111111-1111-1111-1111-111111111111", "binexus-branch-device", DeviceAuthCryptoFormats.TokenType, "unknown-kid", false, false)]
    [InlineData("binexus-branch-device/11111111-1111-1111-1111-111111111111", "binexus-branch-device", DeviceAuthCryptoFormats.TokenType, "test-dat-1", true, false)]
    public void Forged_dat_claim_matrix_is_strictly_validated(
        string issuer,
        string audience,
        string tokenType,
        string kid,
        bool expired,
        bool valid)
    {
        var validator = CreateValidator();
        var token = Mint(issuer, audience, tokenType, kid, expired);

        if (valid)
        {
            validator.Validate(token, DateTimeOffset.UtcNow).Identity!.IsAuthenticated.Should().BeTrue();
            return;
        }

        var act = () => validator.Validate(token, DateTimeOffset.UtcNow);
        act.Should().Throw<DeviceAuthException>()
            .Which.Code.Should().Be(expired
                ? DeviceAuthErrorCodes.DeviceTokenExpired
                : DeviceAuthErrorCodes.DeviceTokenInvalid);
    }

    [Fact]
    public void Malformed_dat_is_device_token_invalid()
    {
        var act = () => CreateValidator().Validate("not-a-jwt", DateTimeOffset.UtcNow);

        act.Should().Throw<DeviceAuthException>()
            .Which.Code.Should().Be(DeviceAuthErrorCodes.DeviceTokenInvalid);
    }

    [Fact]
    public void Dat_signed_with_different_key_material_and_current_kid_is_invalid()
    {
        var token = Mint(
            "binexus-branch-device/11111111-1111-1111-1111-111111111111",
            "binexus-branch-device",
            DeviceAuthCryptoFormats.TokenType,
            "test-dat-1",
            expired: false,
            signingKey: "different-branch-device-auth-signing-key-32b");

        var act = () => CreateValidator().Validate(token, DateTimeOffset.UtcNow);

        act.Should().Throw<DeviceAuthException>()
            .Which.Code.Should().Be(DeviceAuthErrorCodes.DeviceTokenInvalid);
    }

    private static DeviceAccessTokenValidator CreateValidator() =>
        new(Options.Create(new BranchDeviceAuthOptions
        {
            CurrentKeyId = "test-dat-1",
            ClockSkewSeconds = 0,
            SigningKeys =
            [
                new BranchDeviceAuthSigningKey { KeyId = "test-dat-1", Key = SigningKey },
            ],
        }));

    private static string Mint(
        string issuer,
        string audience,
        string tokenType,
        string kid,
        bool expired,
        string signingKey = SigningKey)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString("D")),
                new Claim("branch_instance_id", InstanceId.ToString("D")),
                new Claim("device_security_stamp", "stamp"),
                new Claim("token_type", tokenType),
            ],
            notBefore: expired ? now.AddMinutes(-5) : now.AddMinutes(-1),
            expires: expired ? now.AddMinutes(-1) : now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        token.Header["kid"] = kid;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
