using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.Crypto;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class DevicePairingEndToEndTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private const string SigningKey = "integration-test-signing-key-with-more-than-32-bytes";
    private const string Pepper = "integration-test-branch-pairing-pepper-0000";

    [Fact]
    public async Task Full_ceremony_requires_admin_approval_and_never_sends_credential_raw()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();

        var request = await device.ExchangeAsync(machine, session, "Caja 1");
        request.Status.Should().Be(DevicePairingRequest.PendingApprovalStatus);

        // Admin sees exactly the fingerprint the device will show in PR 5.
        var review = await admin.GetFromJsonAsync<RequestDto>($"/branch/pairing/requests/{request.PairingRequestId:D}");
        review!.DeviceFingerprintShort.Should().Be(device.FingerprintShort);
        review.DeviceFingerprintShort.Should().Be(request.DeviceFingerprintShort);

        var approve = await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        var confirm = await device.PollAndConfirmAsync(machine, request);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var storedDevice = await db.BranchDevices.AsNoTracking().SingleAsync(x => x.Id == device.DeviceId);
        storedDevice.Status.Should().Be(BranchDevice.ActiveStatus);
        storedDevice.CredentialHash.Should().Be(device.CredentialHash);
        var terminal = await db.BranchTerminals.AsNoTracking().SingleAsync(x => x.DeviceId == device.DeviceId);
        terminal.Status.Should().Be(BranchTerminal.ActiveStatus);
        terminal.Name.Should().Be("Caja 1");

        // Confirm is idempotent for a lost response.
        var again = await device.ConfirmLastAsync(machine);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<ConfirmDto>())!.AlreadyActive.Should().BeTrue();
    }

    [Fact]
    public async Task Stolen_code_with_attacker_key_cannot_activate_because_admin_rejects()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var attacker = new SimulatedPairingClient();
        var request = await attacker.ExchangeAsync(machine, session, "Rogue");

        // Proof-of-possession succeeds, but the request only waits for approval — no device exists yet.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            (await db.BranchDevices.AsNoTracking().AnyAsync()).Should().BeFalse();
        }

        var reject = await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/reject", null);
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await attacker.GetStatusAsync(machine, request);
        status.Status.Should().Be(DevicePairingRequest.RejectedStatus);

        // Even a forged confirm attempt fails.
        var confirm = await attacker.TryConfirmWithFakeChallengeAsync(machine, request);
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lost_exchange_response_retry_rotates_status_token_keeping_request_and_credential()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();
        var first = await device.ExchangeAsync(machine, session, "Caja 1");
        var firstToken = device.StatusToken;

        var second = await device.ExchangeAsync(machine, session, "Caja 1");
        second.PairingRequestId.Should().Be(first.PairingRequestId);
        device.StatusToken.Should().NotBe(firstToken);

        // Old token is now invalid; the new one works.
        var oldTokenStatus = await machine.PostAsJsonAsync(
            $"/branch/pairing/requests/{first.PairingRequestId:D}/status",
            new { pairingStatusToken = firstToken });
        oldTokenStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var newTokenStatus = await device.GetStatusAsync(machine, first);
        newTokenStatus.Status.Should().Be(DevicePairingRequest.PendingApprovalStatus);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var request = await db.DevicePairingRequests.AsNoTracking().SingleAsync(x => x.Id == first.PairingRequestId);
        request.PublicKeyFingerprint.Should().Be(EcdsaP256ActivationCrypto.Fingerprint(device.PublicKey));
        request.CredentialHash.Should().Be(device.CredentialHash);
    }

    [Fact]
    public async Task Revoked_material_cannot_be_reused_but_fresh_material_can_repair()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var device = new SimulatedPairingClient();
        await PairFullyAsync(context, admin, machine, device, "Caja 1");

        var revoke = await admin.PostAsync($"/branch/devices/{device.DeviceId:D}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        // Same DeviceId + key + credential must be rejected on a brand new session.
        var reuseSession = await CreateSessionAsync(admin);
        var reuse = await device.TryExchangeAsync(machine, reuseSession, "Caja 1");
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Fresh material re-pairs, reusing the freed terminal name.
        var freshDevice = new SimulatedPairingClient();
        await PairFullyAsync(context, admin, machine, freshDevice, "Caja 1");
        freshDevice.DeviceId.Should().NotBe(device.DeviceId);
    }

    [Fact]
    public async Task Pairing_endpoints_are_branch_only_and_require_active_instance()
    {
        // Branch not yet active → admin session creation is rejected.
        var ready = await StartBranchAsync(active: false);
        using var readyAdmin = ready.CreateAdminClient();
        var rejected = await readyAdmin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("BRANCH_NOT_ACTIVE");

        // Cloud host does not expose Branch pairing endpoints at all.
        await using var cloud = CreateCloudFactory();
        var cloudCall = await cloud.CreateClient().PostAsJsonAsync("/branch/pairing/sessions", new { });
        cloudCall.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_approvals_produce_a_single_terminal()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();
        var request = await device.ExchangeAsync(machine, session, "Caja 1");

        var url = $"/branch/pairing/requests/{request.PairingRequestId:D}/approve";
        var results = await Task.WhenAll(
            admin.PostAsync(url, null),
            admin.PostAsync(url, null));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var terminalIds = new List<Guid>();
        foreach (var result in results)
        {
            terminalIds.Add((await result.Content.ReadFromJsonAsync<ApproveDto>())!.TerminalId);
        }

        terminalIds.Distinct().Should().HaveCount(1);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.BranchTerminals.AsNoTracking().CountAsync(x => x.DeviceId == device.DeviceId)).Should().Be(1);
        (await db.BranchDevices.AsNoTracking().CountAsync(x => x.Id == device.DeviceId)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_confirms_leave_device_active_once()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();
        var request = await device.ExchangeAsync(machine, session, "Caja 1");
        (await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null))
            .EnsureSuccessStatusCode();

        // Prime the confirm state, then fire it twice concurrently.
        (await device.PollAndConfirmAsync(machine, request)).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(device.ConfirmLastAsync(machine), device.ConfirmLastAsync(machine));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.BranchDevices.AsNoTracking().SingleAsync(x => x.Id == device.DeviceId)).Status
            .Should().Be(BranchDevice.ActiveStatus);
    }

    [Fact]
    public async Task Restart_after_approve_allows_receipt_reissue_with_pop_then_confirm()
    {
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await ResetAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            instance.Activate(tenantId, branchId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var device = new SimulatedPairingClient();
        Guid pairingRequestId;
        await using (var context = new BranchContext(CreateBranchFactory(), tenantId, branchId, userId))
        {
            using var admin = context.CreateAdminClient();
            using var machine = context.Factory.CreateClient();
            var session = await CreateSessionAsync(admin);
            var request = await device.ExchangeAsync(machine, session, "Caja 1");
            pairingRequestId = request.PairingRequestId;
            (await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null))
                .EnsureSuccessStatusCode();
        }

        // New process = empty in-memory vault; persisted Approved request remains in PostgreSQL.
        await using var restarted = new BranchContext(CreateBranchFactory(), tenantId, branchId, userId);
        using var restartedMachine = restarted.Factory.CreateClient();
        var status = await restartedMachine.PostAsJsonAsync(
            $"/branch/pairing/requests/{pairingRequestId:D}/status",
            new { pairingStatusToken = device.StatusToken });
        status.EnsureSuccessStatusCode();
        (await status.Content.ReadFromJsonAsync<StatusDto>())!.PairingReceipt.Should().BeNull();

        var reissued = await device.ReissueReceiptAsync(restartedMachine, pairingRequestId);
        reissued.PairingReceipt.Should().NotBeNullOrEmpty();

        var confirm = await device.ConfirmWithReceiptAsync(
            restartedMachine,
            pairingRequestId,
            reissued.BranchInstanceId,
            reissued.TerminalId,
            reissued.ConfirmationChallengeId,
            reissued.ConfirmationNonce,
            reissued.ConfirmationExpiresAtUtc,
            reissued.PairingReceipt);
        confirm.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reissued_receipt_invalidates_the_previous_one()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();
        var request = await device.ExchangeAsync(machine, session, "Caja 1");
        (await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null))
            .EnsureSuccessStatusCode();

        var status = await device.GetStatusAsync(machine, request);
        var receiptA = status.PairingReceipt!;
        var reissued = await device.ReissueReceiptAsync(machine, request.PairingRequestId);

        var staleConfirm = await device.ConfirmWithReceiptAsync(
            machine,
            request.PairingRequestId,
            reissued.BranchInstanceId,
            reissued.TerminalId,
            reissued.ConfirmationChallengeId,
            reissued.ConfirmationNonce,
            reissued.ConfirmationExpiresAtUtc,
            receiptA);
        staleConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var freshConfirm = await device.ConfirmWithReceiptAsync(
            machine,
            request.PairingRequestId,
            reissued.BranchInstanceId,
            reissued.TerminalId,
            reissued.ConfirmationChallengeId,
            reissued.ConfirmationNonce,
            reissued.ConfirmationExpiresAtUtc,
            reissued.PairingReceipt);
        freshConfirm.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Concurrent_reissues_leave_a_single_receipt_hash()
    {
        var context = await StartBranchAsync();
        using var admin = context.CreateAdminClient();
        using var machine = context.Factory.CreateClient();

        var session = await CreateSessionAsync(admin);
        var device = new SimulatedPairingClient();
        var request = await device.ExchangeAsync(machine, session, "Caja 1");
        (await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null))
            .EnsureSuccessStatusCode();

        var challengeA = await device.CreateReceiptReissueChallengeAsync(machine, request.PairingRequestId);
        var challengeB = await device.CreateReceiptReissueChallengeAsync(machine, request.PairingRequestId);

        var results = await Task.WhenAll(
            device.ReissueWithChallengeAsync(machine, request.PairingRequestId, challengeA),
            device.ReissueWithChallengeAsync(machine, request.PairingRequestId, challengeB));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var stored = await db.DevicePairingRequests.AsNoTracking().SingleAsync(x => x.Id == request.PairingRequestId);
        stored.PairingReceiptHash.Should().NotBeNull();
        (await db.DevicePairingChallenges.AsNoTracking()
            .CountAsync(x => x.PairingRequestId == request.PairingRequestId
                && x.Phase == DevicePairingChallenge.ConfirmationPhase
                && x.ConsumedAtUtc == null)).Should().Be(1);
    }

    private static async Task PairFullyAsync(
        BranchContext context,
        HttpClient admin,
        HttpClient machine,
        SimulatedPairingClient device,
        string terminalName)
    {
        var session = await CreateSessionAsync(admin);
        var request = await device.ExchangeAsync(machine, session, terminalName);
        var approve = await admin.PostAsync($"/branch/pairing/requests/{request.PairingRequestId:D}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var confirm = await device.PollAndConfirmAsync(machine, request);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
    }

    private static async Task<SessionDto> CreateSessionAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private async Task<BranchContext> StartBranchAsync(bool active = true)
    {
        await ResetAsync();
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            if (active)
            {
                instance.Activate(tenantId, branchId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            }

            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var factory = CreateBranchFactory();
        return new BranchContext(factory, tenantId, branchId, userId);
    }

    private WebApplicationFactory<Program> CreateBranchFactory() =>
        new PairingTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", Pepper);
            builder.UseSetting("BranchDeviceAuth:CurrentKeyId", "test-dat-1");
            builder.UseSetting("BranchDeviceAuth:SigningKeys:0:KeyId", "test-dat-1");
            builder.UseSetting("BranchDeviceAuth:SigningKeys:0:Key", "integration-test-branch-device-auth-signing-key-32b");
            builder.UseSetting("BranchDeviceAuth:AllowInsecureBranchTransport", "true");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

    private WebApplicationFactory<Program> CreateCloudFactory() =>
        new PairingTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
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
            TRUNCATE TABLE device_auth_challenges, device_pairing_challenges, device_pairing_requests, device_pairing_sessions,
                branch_devices, branch_terminals, branch_instances CASCADE;
            """);
    }

    private static string CreateAdminJwt(Guid tenantId, Guid branchId, Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "binexus",
            audience: "binexus-api",
            claims:
            [
                new Claim("sub", userId.ToString("D")),
                new Claim("tenantId", tenantId.ToString("D")),
                new Claim("branchId", branchId.ToString("D")),
                new Claim("role", "ADMIN"),
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class BranchContext(WebApplicationFactory<Program> factory, Guid tenantId, Guid branchId, Guid userId)
        : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public HttpClient CreateAdminClient()
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, branchId, userId));
            return client;
        }

        public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
    }

    private sealed class PairingTestFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }

    /// <summary>Device side of the ceremony. The raw credential and private key never leave this object.</summary>
    private sealed class SimulatedPairingClient
    {
        private readonly ActivationKeyPair _keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        private readonly string _credential = PairingSecret.Generate();
        private ExchangeDto? _lastExchange;
        private ConfirmChallengeState? _confirmState;

        public Guid DeviceId { get; } = Guid.CreateVersion7();
        public string PublicKey => _keyPair.PublicKey;
        public string CredentialHash => PairingSecret.Hash(_credential);
        public string FingerprintShort => DevicePairingFingerprint.ToShortDisplay(EcdsaP256ActivationCrypto.Fingerprint(PublicKey));
        public string StatusToken { get; private set; } = string.Empty;

        public async Task<ExchangeDto> ExchangeAsync(HttpClient machine, SessionDto session, string terminalName)
        {
            var response = await TryExchangeAsync(machine, session, terminalName);
            response.EnsureSuccessStatusCode();
            var exchange = (await response.Content.ReadFromJsonAsync<ExchangeDto>())!;
            StatusToken = exchange.PairingStatusToken;
            _lastExchange = exchange;
            return exchange;
        }

        public async Task<HttpResponseMessage> TryExchangeAsync(HttpClient machine, SessionDto session, string terminalName)
        {
            var challenge = await CreateChallengeAsync(machine, session);
            var payload = CanonicalDevicePairingChallengeCodec.EncodeExchange(new CanonicalDevicePairingExchangeChallenge(
                challenge.ChallengeId,
                challenge.BranchInstanceId,
                session.PairingSessionId,
                DeviceId,
                EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                CredentialHash,
                challenge.Nonce,
                challenge.ExpiresAtUtc));
            var signature = EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8);
            return await machine.PostAsJsonAsync("/branch/pairing/exchange", new
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
        }

        public async Task<StatusDto> GetStatusAsync(HttpClient machine, ExchangeDto request)
        {
            var response = await machine.PostAsJsonAsync(
                $"/branch/pairing/requests/{request.PairingRequestId:D}/status",
                new { pairingStatusToken = StatusToken });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StatusDto>())!;
        }

        public async Task<HttpResponseMessage> PollAndConfirmAsync(HttpClient machine, ExchangeDto request)
        {
            var status = await GetStatusAsync(machine, request);
            status.Status.Should().Be(DevicePairingRequest.ApprovedStatus);
            status.PairingReceipt.Should().NotBeNullOrEmpty();

            _confirmState = new ConfirmChallengeState(
                request.PairingRequestId,
                status.ConfirmationChallengeId!.Value,
                BuildConfirmSignature(request, status),
                status.PairingReceipt!,
                StatusToken);
            return await SendConfirmAsync(machine, _confirmState);
        }

        public async Task<HttpResponseMessage> ConfirmLastAsync(HttpClient machine) =>
            await SendConfirmAsync(machine, _confirmState!);

        public async Task<ReissueDto> ReissueReceiptAsync(HttpClient machine, Guid pairingRequestId)
        {
            var challenge = await CreateReceiptReissueChallengeAsync(machine, pairingRequestId);
            var response = await ReissueWithChallengeAsync(machine, pairingRequestId, challenge);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ReissueDto>())!;
        }

        public async Task<ReissueChallengeDto> CreateReceiptReissueChallengeAsync(HttpClient machine, Guid pairingRequestId)
        {
            var response = await machine.PostAsJsonAsync(
                $"/branch/pairing/requests/{pairingRequestId:D}/receipt/challenges",
                new { pairingStatusToken = StatusToken });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ReissueChallengeDto>())!;
        }

        public async Task<HttpResponseMessage> ReissueWithChallengeAsync(
            HttpClient machine,
            Guid pairingRequestId,
            ReissueChallengeDto challenge)
        {
            var payload = CanonicalDevicePairingChallengeCodec.EncodeReceiptReissue(
                new CanonicalDevicePairingReceiptReissueChallenge(
                    challenge.ChallengeId,
                    pairingRequestId,
                    challenge.BranchInstanceId,
                    DeviceId,
                    EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                    CredentialHash,
                    challenge.Nonce,
                    challenge.ExpiresAtUtc));
            var signature = EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8);
            return await machine.PostAsJsonAsync(
                $"/branch/pairing/requests/{pairingRequestId:D}/receipt/reissue",
                new
                {
                    pairingStatusToken = StatusToken,
                    reissueChallengeId = challenge.ChallengeId,
                    signature,
                });
        }

        public async Task<HttpResponseMessage> ConfirmWithReceiptAsync(
            HttpClient machine,
            Guid pairingRequestId,
            Guid branchInstanceId,
            Guid terminalId,
            Guid confirmationChallengeId,
            string confirmationNonce,
            DateTimeOffset confirmationExpiresAtUtc,
            string pairingReceipt)
        {
            var payload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(
                new CanonicalDevicePairingConfirmChallenge(
                    confirmationChallengeId,
                    pairingRequestId,
                    branchInstanceId,
                    DeviceId,
                    terminalId,
                    EcdsaP256ActivationCrypto.Fingerprint(PublicKey),
                    CredentialHash,
                    PairingSecret.Hash(pairingReceipt),
                    confirmationNonce,
                    confirmationExpiresAtUtc));
            return await machine.PostAsJsonAsync("/branch/pairing/confirm", new
            {
                pairingRequestId,
                confirmationChallengeId,
                signature = EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8),
                pairingReceipt,
                pairingStatusToken = StatusToken,
            });
        }

        public async Task<HttpResponseMessage> TryConfirmWithFakeChallengeAsync(HttpClient machine, ExchangeDto request) =>
            await machine.PostAsJsonAsync("/branch/pairing/confirm", new
            {
                pairingRequestId = request.PairingRequestId,
                confirmationChallengeId = Guid.CreateVersion7(),
                signature = "not-a-signature",
                pairingReceipt = PairingSecret.Generate(),
                pairingStatusToken = StatusToken,
            });

        private string BuildConfirmSignature(ExchangeDto request, StatusDto status)
        {
            var payload = CanonicalDevicePairingChallengeCodec.EncodeConfirmation(new CanonicalDevicePairingConfirmChallenge(
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
            return EcdsaP256ActivationCrypto.Sign(payload, _keyPair.PrivateKeyPkcs8);
        }

        private static async Task<HttpResponseMessage> SendConfirmAsync(HttpClient machine, ConfirmChallengeState state) =>
            await machine.PostAsJsonAsync("/branch/pairing/confirm", new
            {
                pairingRequestId = state.PairingRequestId,
                confirmationChallengeId = state.ConfirmationChallengeId,
                signature = state.Signature,
                pairingReceipt = state.PairingReceipt,
                pairingStatusToken = state.StatusToken,
            });

        private async Task<ChallengeDto> CreateChallengeAsync(HttpClient machine, SessionDto session)
        {
            var response = await machine.PostAsJsonAsync("/branch/pairing/challenges", new
            {
                pairingSessionId = session.PairingSessionId,
                pairingCode = session.PairingCode,
                deviceId = DeviceId,
                publicKey = PublicKey,
                credentialHash = CredentialHash,
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ChallengeDto>())!;
        }
    }

    private sealed record ConfirmChallengeState(
        Guid PairingRequestId,
        Guid ConfirmationChallengeId,
        string Signature,
        string PairingReceipt,
        string StatusToken);

    private sealed record SessionDto(
        [property: JsonPropertyName("pairingSessionId")] Guid PairingSessionId,
        [property: JsonPropertyName("pairingCode")] string PairingCode);

    private sealed record ChallengeDto(
        [property: JsonPropertyName("challengeId")] Guid ChallengeId,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

    private sealed record ExchangeDto(
        [property: JsonPropertyName("pairingRequestId")] Guid PairingRequestId,
        [property: JsonPropertyName("deviceFingerprintShort")] string DeviceFingerprintShort,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("pairingStatusToken")] string PairingStatusToken);

    private sealed record StatusDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("terminalId")] Guid? TerminalId,
        [property: JsonPropertyName("confirmationChallengeId")] Guid? ConfirmationChallengeId,
        [property: JsonPropertyName("confirmationNonce")] string? ConfirmationNonce,
        [property: JsonPropertyName("confirmationExpiresAtUtc")] DateTimeOffset? ConfirmationExpiresAtUtc,
        [property: JsonPropertyName("pairingReceipt")] string? PairingReceipt);

    private sealed record RequestDto(
        [property: JsonPropertyName("deviceFingerprintShort")] string DeviceFingerprintShort,
        [property: JsonPropertyName("status")] string Status);

    private sealed record ConfirmDto(
        [property: JsonPropertyName("alreadyActive")] bool AlreadyActive);

    private sealed record ApproveDto(
        [property: JsonPropertyName("terminalId")] Guid TerminalId);

    private sealed record ReissueChallengeDto(
        [property: JsonPropertyName("challengeId")] Guid ChallengeId,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

    private sealed record ReissueDto(
        [property: JsonPropertyName("pairingRequestId")] Guid PairingRequestId,
        [property: JsonPropertyName("branchInstanceId")] Guid BranchInstanceId,
        [property: JsonPropertyName("terminalId")] Guid TerminalId,
        [property: JsonPropertyName("pairingReceipt")] string PairingReceipt,
        [property: JsonPropertyName("confirmationChallengeId")] Guid ConfirmationChallengeId,
        [property: JsonPropertyName("confirmationNonce")] string ConfirmationNonce,
        [property: JsonPropertyName("confirmationExpiresAtUtc")] DateTimeOffset ConfirmationExpiresAtUtc);
}
