using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Branching.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class DeviceAuthHttpMatrixTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Tokens_rejects_signature_from_a_different_private_key_without_leaking_device_state()
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var challenge = await device.CreateChallengeAsync(machine);
            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = device.DeviceId,
                signature = new DeviceAuthEndToEndTests.SimulatedDeviceAuthClient().SignChallenge(challenge),
                protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            });

            await AssertGenericProofInvalidAsync(response);
        }
    }

    [Fact]
    public async Task Tokens_rejects_mismatched_device_id_with_generic_proof_invalid()
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var challenge = await device.CreateChallengeAsync(machine);
            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = Guid.CreateVersion7(),
                signature = device.SignChallenge(challenge),
                protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            });

            await AssertGenericProofInvalidAsync(response);
        }
    }

    [Theory]
    [InlineData("not-base64url-signature", "v1")]
    [InlineData("", "unknown-version")]
    public async Task Tokens_rejects_malformed_signature_or_unknown_protocol_version(
        string signature,
        string protocolVersion)
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var challenge = await device.CreateChallengeAsync(machine);
            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = device.DeviceId,
                signature,
                protocolVersion,
            });

            await AssertGenericProofInvalidAsync(response);
        }
    }

    [Fact]
    public async Task Tokens_reports_expired_challenge()
    {
        var test = new DeviceAuthEndToEndTests(fixture);
        var context = await test.StartBranchAsync();
        await using var _ = context;
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();
        var device = new DeviceAuthEndToEndTests.SimulatedDeviceAuthClient();
        await DeviceAuthEndToEndTests.PairFullyAsync(context, admin, machine, device, "Caja Expiry");
        var challenge = await device.CreateChallengeAsync(machine);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE device_auth_challenges SET expires_at_utc = NOW() - INTERVAL '1 second' WHERE id = {challenge.ChallengeId}");
        }

        var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
        {
            challengeId = challenge.ChallengeId,
            deviceId = device.DeviceId,
            signature = device.SignChallenge(challenge),
            protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceChallengeExpired);
    }

    [Fact]
    public async Task Challenges_for_unknown_device_are_generic_and_do_not_enumerate_sensitive_state()
    {
        var test = new DeviceAuthEndToEndTests(fixture);
        var context = await test.StartBranchAsync();
        await using var _ = context;
        using var machine = context.Factory.CreateClient();

        var response = await machine.PostAsJsonAsync(
            "/branch/device-auth/challenges",
            new { deviceId = Guid.CreateVersion7() });

        await AssertGenericProofInvalidAsync(response);
    }

    [Theory]
    [InlineData("unknown-device")]
    [InlineData("pending-confirmation")]
    [InlineData("revoked")]
    [InlineData("wrong-private-key")]
    public async Task Public_device_auth_failures_are_indistinguishable(
        string scenario)
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var baseline = await machine.PostAsJsonAsync(
                "/branch/device-auth/challenges",
                new { deviceId = Guid.CreateVersion7() });
            HttpResponseMessage response;
            switch (scenario)
            {
                case "unknown-device":
                    response = baseline;
                    break;
                case "pending-confirmation":
                    await SetDeviceStatusAsync(device.DeviceId, BranchDevice.PendingConfirmationStatus);
                    response = await machine.PostAsJsonAsync(
                        "/branch/device-auth/challenges",
                        new { deviceId = device.DeviceId });
                    break;
                case "revoked":
                    await SetDeviceStatusAsync(device.DeviceId, BranchDevice.RevokedStatus);
                    response = await machine.PostAsJsonAsync(
                        "/branch/device-auth/challenges",
                        new { deviceId = device.DeviceId });
                    break;
                case "wrong-private-key":
                    var challenge = await device.CreateChallengeAsync(machine);
                    response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
                    {
                        challengeId = challenge.ChallengeId,
                        deviceId = device.DeviceId,
                        signature = new DeviceAuthEndToEndTests.SimulatedDeviceAuthClient().SignChallenge(challenge),
                        protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            await AssertGenericProofInvalidAsync(baseline);
            await AssertGenericProofInvalidAsync(response);

            // Same public code/status; ignore per-request instance/trace identifiers.
            using var baselineDoc = JsonDocument.Parse(await baseline.Content.ReadAsStringAsync());
            using var responseDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            responseDoc.RootElement.GetProperty("code").GetString()
                .Should().Be(baselineDoc.RootElement.GetProperty("code").GetString());
            responseDoc.RootElement.GetProperty("status").GetInt32()
                .Should().Be(baselineDoc.RootElement.GetProperty("status").GetInt32());
            responseDoc.RootElement.GetProperty("title").GetString()
                .Should().Be(baselineDoc.RootElement.GetProperty("title").GetString());
        }
    }

    [Theory]
    [InlineData(BranchDevice.PendingConfirmationStatus)]
    [InlineData(BranchDevice.RevokedStatus)]
    public async Task Tokens_reject_non_active_devices_without_leaking_device_state(string status)
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var challenge = await device.CreateChallengeAsync(machine);
            await SetDeviceStatusAsync(device.DeviceId, status);

            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = device.DeviceId,
                signature = device.SignChallenge(challenge),
                protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            });

            await AssertGenericProofInvalidAsync(response);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    public async Task Challenges_reject_missing_or_disabled_terminal_without_leaking_binding_state(
        string terminalState)
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
                if (terminalState == "missing")
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM branch_terminals WHERE device_id = {device.DeviceId}");
                }
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE branch_terminals SET status = {BranchTerminal.DisabledStatus} WHERE device_id = {device.DeviceId}");
                }
            }

            var response = await machine.PostAsJsonAsync(
                "/branch/device-auth/challenges",
                new { deviceId = device.DeviceId });

            await AssertGenericProofInvalidAsync(response);
        }
    }

    [Fact]
    public async Task Tokens_reject_corrupt_terminal_binding()
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var challenge = await device.CreateChallengeAsync(machine);
            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE branch_terminals SET device_id = {Guid.CreateVersion7()} WHERE device_id = {device.DeviceId}");
            }

            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = device.DeviceId,
                signature = device.SignChallenge(challenge),
                protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceBindingInvalid);
        }
    }

    [Fact]
    public async Task Me_rejects_dat_stamped_for_a_different_branch_instance()
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        using (var client = context.Factory.CreateClient())
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
                $"Bearer {MintDat(device.DeviceId, Guid.CreateVersion7())}");

            var response = await client.GetAsync("/branch/device-auth/me");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceBranchMismatch);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operational_routes_reject_user_tenant_or_branch_mismatch_with_valid_dat(
        bool wrongTenant)
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var dat = await device.IssueDatAsync(machine);
            using var client = context.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new(
                "Bearer",
                DeviceAuthEndToEndTests.CreateUserJwt(
                    wrongTenant ? Guid.CreateVersion7() : context.TenantId,
                    wrongTenant ? context.BranchId : Guid.CreateVersion7(),
                    context.UserId));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
                $"Bearer {dat.AccessToken}");

            var response = await client.GetAsync("/inventory/stock");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.UserBranchMismatch);
        }
    }

    [Fact]
    public async Task Operational_routes_reject_invalid_user_jwt_with_valid_dat()
    {
        var (context, machine, device) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        {
            var dat = await device.IssueDatAsync(machine);
            using var client = context.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-valid-user-jwt");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
                $"Bearer {dat.AccessToken}");

            var response = await client.GetAsync("/inventory/stock");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.UserAuthRequired);
        }
    }

    [Fact]
    public async Task Operational_routes_reject_invalid_dat_with_valid_user_jwt()
    {
        var (context, machine, _) = await StartPairedDeviceAsync();
        await using var _ = context;
        using (machine)
        using (var client = context.CreateUserClient())
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
                "Bearer not-a-dat");

            var response = await client.GetAsync("/inventory/stock");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceAuthRequired);
        }
    }

    private async Task<(DeviceAuthEndToEndTests.BranchContext Context, HttpClient Machine,
        DeviceAuthEndToEndTests.SimulatedDeviceAuthClient Device)> StartPairedDeviceAsync()
    {
        var test = new DeviceAuthEndToEndTests(fixture);
        var context = await test.StartBranchAsync();
        var admin = context.CreateAdminClient();
        var machine = context.Factory.CreateClient();
        var device = new DeviceAuthEndToEndTests.SimulatedDeviceAuthClient();
        await DeviceAuthEndToEndTests.PairFullyAsync(context, admin, machine, device, "Caja Matrix");
        admin.Dispose();
        return (context, machine, device);
    }

    private async Task SetDeviceStatusAsync(Guid deviceId, string status)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE branch_devices SET status = {status} WHERE id = {deviceId}");
    }

    private static string MintDat(Guid deviceId, Guid branchInstanceId)
    {
        var token = new JwtSecurityToken(
            issuer: $"binexus-branch-device/{branchInstanceId:D}",
            audience: DeviceAuthCryptoFormats.TokenAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, deviceId.ToString("D")),
                new Claim("branch_instance_id", branchInstanceId.ToString("D")),
                new Claim("device_security_stamp", "irrelevant-for-instance-mismatch"),
                new Claim("token_type", DeviceAuthCryptoFormats.TokenType),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-branch-device-auth-signing-key-32b")),
                SecurityAlgorithms.HmacSha256));
        token.Header["kid"] = "test-dat-1";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertGenericProofInvalidAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(DeviceAuthErrorCodes.DeviceProofInvalid);
        body.Should().NotContainEquivalentOf("revoked");
        body.Should().NotContainEquivalentOf("pending");
        body.Should().NotContainEquivalentOf("fingerprint");
        body.Should().NotContainEquivalentOf("credentialHash");
        body.Should().NotContainEquivalentOf("securityStamp");
        body.Should().NotContainEquivalentOf("not found");
    }
}
