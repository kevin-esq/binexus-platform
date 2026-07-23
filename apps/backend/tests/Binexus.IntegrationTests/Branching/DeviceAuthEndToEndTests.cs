using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class DeviceAuthEndToEndTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private const string UserSigningKey = "integration-test-signing-key-with-more-than-32-bytes";
    private const string DatSigningKey = "integration-test-branch-device-auth-signing-key-32b";
    private const string Pepper = "integration-test-branch-pairing-pepper-0000";

    [Fact]
    public async Task Issue_me_revoke_rejects_unexpired_dat()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja DAT");

        var tokens = await device.IssueDatAsync(machine);
        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.TokenType.Should().Be(DeviceAuthCryptoFormats.TokenType);

        using var deviceClient = context.Factory.CreateClient();
        deviceClient.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");

        var me = await deviceClient.GetAsync("/branch/device-auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync());
        var meBody = (await me.Content.ReadFromJsonAsync<MeDto>())!;
        meBody.DeviceId.Should().Be(device.DeviceId);
        meBody.Status.Should().Be(BranchDevice.ActiveStatus);

        var revoke = await admin.PostAsync($"/branch/devices/{device.DeviceId:D}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var meAfter = await deviceClient.GetAsync("/branch/device-auth/me");
        meAfter.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await meAfter.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceRevoked);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var stored = await db.BranchDevices.AsNoTracking().SingleAsync(x => x.Id == device.DeviceId);
            stored.SecurityStamp.Should().NotBeNullOrWhiteSpace();
            stored.Status.Should().Be(BranchDevice.RevokedStatus);
        }
    }

    [Fact]
    public async Task Disable_terminal_invalidates_live_dat_via_stamp()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Disable");
        var tokens = await device.IssueDatAsync(machine);

        using var deviceClient = context.Factory.CreateClient();
        deviceClient.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        (await deviceClient.GetAsync("/branch/device-auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid terminalId;
        string stampBefore;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var stored = await db.BranchDevices.AsNoTracking().SingleAsync(x => x.Id == device.DeviceId);
            stampBefore = stored.SecurityStamp;
            terminalId = (await db.BranchTerminals.AsNoTracking().SingleAsync(x => x.DeviceId == device.DeviceId)).Id;
        }

        var disable = await admin.PostAsync($"/branch/terminals/{terminalId:D}/disable", null);
        disable.StatusCode.Should().Be(HttpStatusCode.OK, await disable.Content.ReadAsStringAsync());

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var stored = await db.BranchDevices.AsNoTracking().SingleAsync(x => x.Id == device.DeviceId);
            stored.SecurityStamp.Should().NotBe(stampBefore);
        }

        var meAfter = await deviceClient.GetAsync("/branch/device-auth/me");
        meAfter.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        (await meAfter.Content.ReadAsStringAsync()).Should().Match(s =>
            s.Contains(DeviceAuthErrorCodes.DeviceTerminalDisabled)
            || s.Contains(DeviceAuthErrorCodes.DeviceTokenInvalid)
            || s.Contains(DeviceAuthErrorCodes.DeviceTerminalMissing));
    }

    [Fact]
    public async Task Rebind_terminal_invalidates_live_dat_and_allows_new_dat()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Rebind A");
        var tokens = await device.IssueDatAsync(machine);

        using var deviceClient = context.Factory.CreateClient();
        deviceClient.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        (await deviceClient.GetAsync("/branch/device-auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var rebind = await admin.PostAsJsonAsync(
            $"/branch/devices/{device.DeviceId:D}/terminals/rebind",
            new { terminalName = "Caja Rebind B" });
        rebind.StatusCode.Should().Be(HttpStatusCode.OK, await rebind.Content.ReadAsStringAsync());

        var stale = await deviceClient.GetAsync("/branch/device-auth/me");
        stale.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        var fresh = await device.IssueDatAsync(machine);
        using var freshClient = context.Factory.CreateClient();
        freshClient.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {fresh.AccessToken}");
        (await freshClient.GetAsync("/branch/device-auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/sales/sessions/current")]
    [InlineData("/inventory/stock")]
    [InlineData("/orders")]
    [InlineData("/warehouse/picking-tasks")]
    [InlineData("/logistics/delivery-routes")]
    public async Task Branch_operational_modules_require_device_and_user(string path)
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();
        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Mod");
        var tokens = await device.IssueDatAsync(machine);

        using var userOnly = context.CreateUserClient();
        var userOnlyResponse = await userOnly.GetAsync(path);
        userOnlyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await userOnlyResponse.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceAuthRequired);

        using var datOnly = context.Factory.CreateClient();
        datOnly.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        var datOnlyResponse = await datOnly.GetAsync(path);
        datOnlyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await datOnlyResponse.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.UserAuthRequired);

        using var both = context.CreateUserClient();
        both.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        var bothResponse = await both.GetAsync(path);
        var body = await bothResponse.Content.ReadAsStringAsync();
        body.Should().NotContain(DeviceAuthErrorCodes.DeviceAuthRequired);
        body.Should().NotContain(DeviceAuthErrorCodes.UserAuthRequired);
    }

    [Fact]
    public async Task Sales_path_returns_503_when_device_status_cache_miss_is_unavailable()
    {
        var failClosed = new FailClosedSwitch();
        var context = await StartBranchAsync(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDeviceStatusResolver>();
            services.AddSingleton(failClosed);
            services.AddScoped<DeviceStatusResolver>();
            services.AddScoped<IDeviceStatusResolver>(sp => new FailClosedDeviceStatusResolver(
                sp.GetRequiredService<DeviceStatusResolver>(),
                sp.GetRequiredService<FailClosedSwitch>()));
        }));
        await using var _ = context;
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();
        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Status Unavailable");
        var tokens = await device.IssueDatAsync(machine);
        failClosed.Enabled = true;

        using var sales = context.CreateUserClient();
        sales.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        var response = await sales.GetAsync("/sales/sessions/current");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceStatusUnavailable);
    }

    [Fact]
    public async Task Challenge_replay_is_rejected_atomically()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Replay");

        var challenge = await device.CreateChallengeAsync(machine);
        var signature = device.SignChallenge(challenge);

        var first = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
        {
            challengeId = challenge.ChallengeId,
            deviceId = device.DeviceId,
            signature,
            protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var second = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
        {
            challengeId = challenge.ChallengeId,
            deviceId = device.DeviceId,
            signature,
            protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceChallengeReplayed);
    }

    [Fact]
    public async Task Parallel_redeem_of_same_challenge_yields_exactly_one_dat()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Parallel");

        var challenge = await device.CreateChallengeAsync(machine);
        var signature = device.SignChallenge(challenge);
        var body = new
        {
            challengeId = challenge.ChallengeId,
            deviceId = device.DeviceId,
            signature,
            protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
        };

        var results = await Task.WhenAll(
            machine.PostAsJsonAsync("/branch/device-auth/tokens", body),
            machine.PostAsJsonAsync("/branch/device-auth/tokens", body));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task Tokens_body_does_not_accept_client_credential_hash_as_trust_input()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Hash");

        var challenge = await device.CreateChallengeAsync(machine);
        var signature = device.SignChallenge(challenge);

        // Extra properties must be ignored; server reconstructs hash from DB.
        var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
        {
            challengeId = challenge.ChallengeId,
            deviceId = device.DeviceId,
            signature,
            protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            credentialHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_device_challenge_is_generic_proof_invalid()
    {
        var context = await StartBranchAsync();
        using var machine = context.Factory.CreateClient();

        var response = await machine.PostAsJsonAsync(
            "/branch/device-auth/challenges",
            new { deviceId = Guid.CreateVersion7() });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeviceAuthErrorCodes.DeviceProofInvalid);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("not found");
    }

    [Fact]
    public async Task Cloud_runtime_keeps_user_only_sales_and_hides_device_auth()
    {
        await ResetAsync();
        await using var cloud = CreateCloudFactory();
        using var client = cloud.CreateClient();

        var missing = await client.PostAsJsonAsync(
            "/branch/device-auth/challenges",
            new { deviceId = Guid.CreateVersion7() });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateUserJwt(tenantId, branchId, userId));

        // Cloud Sales remains User-authorized (no DAT required). Opening may fail for domain
        // reasons; auth must not demand the device header.
        var open = await client.PostAsJsonAsync(
            "/sales/sessions/open",
            new
            {
                branchId,
                terminalId = $"t-{Guid.NewGuid():N}"[..18],
                openingFloat = 0,
                currency = "MXN",
            });
        open.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        (await open.Content.ReadAsStringAsync()).Should().NotContain(DeviceAuthErrorCodes.DeviceAuthRequired);
    }

    [Fact]
    public async Task Branch_sales_requires_device_and_user()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedDeviceAuthClient();
        await PairFullyAsync(context, admin, machine, device, "Caja Sales");
        var tokens = await device.IssueDatAsync(machine);

        using var userOnly = context.CreateUserClient();
        var userOnlyOpen = await userOnly.PostAsJsonAsync(
            "/sales/sessions/open",
            new
            {
                branchId = context.BranchId,
                terminalId = $"t-{Guid.NewGuid():N}"[..18],
                openingFloat = 0,
                currency = "MXN",
            });
        userOnlyOpen.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await userOnlyOpen.Content.ReadAsStringAsync())
            .Should().Contain(DeviceAuthErrorCodes.DeviceAuthRequired);

        using var both = context.CreateUserClient();
        both.DefaultRequestHeaders.TryAddWithoutValidation(
            DeviceAuthCryptoFormats.DeviceAuthorizationHeader,
            $"Bearer {tokens.AccessToken}");
        var bothOpen = await both.PostAsJsonAsync(
            "/sales/sessions/open",
            new
            {
                branchId = context.BranchId,
                terminalId = $"t-{Guid.NewGuid():N}"[..18],
                openingFloat = 0,
                currency = "MXN",
            });
        // Domain may still reject (terminal string vs Guid binding), but not for missing device auth.
        bothOpen.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        var body = await bothOpen.Content.ReadAsStringAsync();
        body.Should().NotContain(DeviceAuthErrorCodes.DeviceAuthRequired);
        if (bothOpen.StatusCode == HttpStatusCode.Forbidden)
        {
            body.Should().NotContain("DEVICE_");
        }
    }

    internal static async Task PairFullyAsync(
        BranchContext context,
        HttpClient admin,
        HttpClient machine,
        SimulatedDeviceAuthClient device,
        string terminalName)
    {
        var sessionResponse = await admin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        sessionResponse.EnsureSuccessStatusCode();
        var session = (await sessionResponse.Content.ReadFromJsonAsync<SessionDto>())!;
        var request = await device.ExchangeAsync(machine, session, terminalName);
        (await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null))
            .EnsureSuccessStatusCode();
        (await device.PollAndConfirmAsync(machine, request)).EnsureSuccessStatusCode();
    }

    internal async Task<BranchContext> StartBranchAsync(Action<IWebHostBuilder>? configure = null)
    {
        await ResetAsync();
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            instance.Activate(tenantId, branchId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        return new BranchContext(CreateBranchFactory(configure), tenantId, branchId, userId);
    }

    private WebApplicationFactory<Program> CreateBranchFactory(Action<IWebHostBuilder>? configure = null) =>
        new DeviceAuthTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", UserSigningKey);
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", Pepper);
            builder.UseSetting("BranchDeviceAuth:CurrentKeyId", "test-dat-1");
            builder.UseSetting("BranchDeviceAuth:SigningKeys:0:KeyId", "test-dat-1");
            builder.UseSetting("BranchDeviceAuth:SigningKeys:0:Key", DatSigningKey);
            builder.UseSetting("BranchDeviceAuth:AllowInsecureBranchTransport", "true");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            configure?.Invoke(builder);
        });

    private WebApplicationFactory<Program> CreateCloudFactory() =>
        new DeviceAuthTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", UserSigningKey);
            builder.UseSetting("CloudActivation:CodePepper", "integration-test-cloud-activation-pepper-32b");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

    private async Task ResetAsync()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE device_auth_challenges, device_pairing_challenges, device_pairing_requests,
                device_pairing_sessions, branch_devices, branch_terminals, branch_instances CASCADE;
            """);
    }

    internal static string CreateUserJwt(Guid tenantId, Guid branchId, Guid userId, string role = "ADMIN")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(UserSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "binexus",
            audience: "binexus-api",
            claims:
            [
                new Claim("sub", userId.ToString("D")),
                new Claim("tenantId", tenantId.ToString("D")),
                new Claim("branchId", branchId.ToString("D")),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal sealed class BranchContext : IAsyncDisposable
    {
        private readonly Guid _tenantId;
        private readonly Guid _userId;

        public BranchContext(WebApplicationFactory<Program> factory, Guid tenantId, Guid branchId, Guid userId)
        {
            Factory = factory;
            BranchId = branchId;
            _tenantId = tenantId;
            _userId = userId;
        }

        public WebApplicationFactory<Program> Factory { get; }
        public Guid TenantId => _tenantId;
        public Guid BranchId { get; }
        public Guid UserId => _userId;

        public HttpClient CreateAdminClient() => CreateUserClient();

        public HttpClient CreateUserClient()
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateUserJwt(_tenantId, BranchId, _userId));
            return client;
        }

        public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
    }

    private sealed class DeviceAuthTestFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }

    private sealed class FailClosedSwitch
    {
        public bool Enabled { get; set; }
    }

    private sealed class FailClosedDeviceStatusResolver(
        DeviceStatusResolver inner,
        FailClosedSwitch failClosed) : IDeviceStatusResolver
    {
        public Task<DeviceStatusSnapshot> ResolveAsync(
            Guid branchInstanceId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            if (failClosed.Enabled)
            {
                return Task.FromException<DeviceStatusSnapshot>(
                    new DeviceAuthException(
                        DeviceAuthErrorCodes.DeviceStatusUnavailable,
                        "Device status unavailable."));
            }

            return inner.ResolveAsync(branchInstanceId, deviceId, cancellationToken);
        }

        public void Evict(Guid branchInstanceId, Guid deviceId) =>
            inner.Evict(branchInstanceId, deviceId);
    }

    internal sealed class SimulatedDeviceAuthClient
    {
        private readonly ActivationKeyPair _keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        private readonly string _credential = PairingSecret.Generate();
        private string _statusToken = string.Empty;

        public Guid DeviceId { get; } = Guid.CreateVersion7();
        public string PublicKey => _keyPair.PublicKey;
        public string CredentialHash => PairingSecret.Hash(_credential);

        public async Task<ExchangeDto> ExchangeAsync(HttpClient machine, SessionDto session, string terminalName)
        {
            var challengeResponse = await machine.PostAsJsonAsync("/branch/pairing/challenges", new
            {
                pairingSessionId = session.PairingSessionId,
                pairingCode = session.PairingCode,
                deviceId = DeviceId,
                publicKey = PublicKey,
                credentialHash = CredentialHash,
            });
            challengeResponse.EnsureSuccessStatusCode();
            var challenge = (await challengeResponse.Content.ReadFromJsonAsync<PairingChallengeDto>())!;

            var payload = CanonicalDevicePairingChallengeCodec.EncodeExchange(
                new CanonicalDevicePairingExchangeChallenge(
                    challenge.ChallengeId,
                    challenge.BranchInstanceId,
                    session.PairingSessionId,
                    DeviceId,
                    EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                    CredentialHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
            var signature = EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8);
            var exchangeResponse = await machine.PostAsJsonAsync("/branch/pairing/exchange", new
            {
                pairingSessionId = session.PairingSessionId,
                pairingCode = session.PairingCode,
                deviceId = DeviceId,
                publicKey = PublicKey,
                challengeId = challenge.ChallengeId,
                signature,
                credentialHash = CredentialHash,
                terminalName,
            });
            exchangeResponse.EnsureSuccessStatusCode();
            var exchange = (await exchangeResponse.Content.ReadFromJsonAsync<ExchangeDto>())!;
            _statusToken = exchange.PairingStatusToken;
            return exchange;
        }

        public async Task<HttpResponseMessage> PollAndConfirmAsync(HttpClient machine, ExchangeDto request)
        {
            var statusResponse = await machine.PostAsJsonAsync(
                $"/branch/pairing/requests/{request.PairingRequestId:D}/status",
                new { pairingStatusToken = _statusToken });
            statusResponse.EnsureSuccessStatusCode();
            var status = (await statusResponse.Content.ReadFromJsonAsync<StatusDto>())!;

            var payload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(
                new CanonicalDevicePairingConfirmChallenge(
                    status.ConfirmationChallengeId!.Value,
                    request.PairingRequestId,
                    status.BranchInstanceId,
                    DeviceId,
                    status.TerminalId!.Value,
                    EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                    CredentialHash,
                    PairingSecret.Hash(status.PairingReceipt!),
                    status.ConfirmationNonce!,
                    status.ConfirmationExpiresAtUtc!.Value));
            return await machine.PostAsJsonAsync("/branch/pairing/confirm", new
            {
                pairingRequestId = request.PairingRequestId,
                confirmationChallengeId = status.ConfirmationChallengeId,
                signature = EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8),
                pairingReceipt = status.PairingReceipt,
                pairingStatusToken = _statusToken,
            });
        }

        public async Task<ChallengeDto> CreateChallengeAsync(HttpClient machine)
        {
            var response = await machine.PostAsJsonAsync(
                "/branch/device-auth/challenges",
                new { deviceId = DeviceId });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ChallengeDto>())!;
        }

        public string SignChallenge(ChallengeDto challenge)
        {
            var payload = CanonicalDeviceAuthChallengeCodec.Encode(
                new CanonicalDeviceAuthChallenge(
                    challenge.ChallengeId,
                    challenge.BranchInstanceId,
                    DeviceId,
                    EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                    CredentialHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
            return EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8);
        }

        public async Task<TokenDto> IssueDatAsync(HttpClient machine)
        {
            var challenge = await CreateChallengeAsync(machine);
            var response = await machine.PostAsJsonAsync("/branch/device-auth/tokens", new
            {
                challengeId = challenge.ChallengeId,
                deviceId = DeviceId,
                signature = SignChallenge(challenge),
                protocolVersion = DeviceAuthCryptoFormats.ChallengeVersion,
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TokenDto>())!;
        }
    }

    internal sealed record SessionDto(
        [property: JsonPropertyName("pairingSessionId")] Guid PairingSessionId,
        [property: JsonPropertyName("pairingCode")] string PairingCode);

    internal sealed record PairingChallengeDto(
        [property: JsonPropertyName("challengeId")] Guid ChallengeId,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

    internal sealed record ExchangeDto(
        [property: JsonPropertyName("pairingRequestId")] Guid PairingRequestId,
        [property: JsonPropertyName("pairingStatusToken")] string PairingStatusToken);

    internal sealed record StatusDto(
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("terminalId")] Guid? TerminalId,
        [property: JsonPropertyName("confirmationChallengeId")] Guid? ConfirmationChallengeId,
        [property: JsonPropertyName("confirmationNonce")] string? ConfirmationNonce,
        [property: JsonPropertyName("confirmationExpiresAtUtc")] DateTimeOffset? ConfirmationExpiresAtUtc,
        [property: JsonPropertyName("pairingReceipt")] string? PairingReceipt);

    internal sealed record ChallengeDto(
        [property: JsonPropertyName("challengeId")] Guid ChallengeId,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

    internal sealed record TokenDto(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("tokenType")] string TokenType);

    private sealed record MeDto(
        [property: JsonPropertyName("deviceId")] Guid DeviceId,
        [property: JsonPropertyName("status")] string Status);
}
