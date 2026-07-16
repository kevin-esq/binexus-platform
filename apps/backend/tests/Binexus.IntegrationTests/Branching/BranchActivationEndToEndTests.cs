using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Domain;
using Binexus.Platform.Branching.Client;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Credentials;
using Binexus.Platform.Branching.Crypto;
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
public sealed class BranchActivationEndToEndTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Dual_host_activate_confirm_health_idempotent_and_second_instance_rejected()
    {
        await ResetAsync();
        var (tenantId, businessBranchId, userId) = await SeedTenantBranchAsync();

        await using var cloudFactory = CreateCloudFactory();
        using var cloudClient = cloudFactory.CreateClient();
        cloudClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, userId));

        var generate = await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId = businessBranchId });
        generate.StatusCode.Should().Be(HttpStatusCode.OK);
        var generated = await generate.Content.ReadFromJsonAsync<GenerateDto>();
        generated.Should().NotBeNull();

        await using var branchFactory = CreateBranchFactoryWiredTo(cloudFactory);
        using var branchClient = branchFactory.CreateClient();

        var readyHealth = await branchClient.GetAsync("/health/branch");
        readyHealth.StatusCode.Should().Be(HttpStatusCode.OK);
        var readyBody = await readyHealth.Content.ReadFromJsonAsync<BranchHealthDto>();
        readyBody!.Status.Should().Be("ReadyForActivation");

        var activate = await branchClient.PostAsJsonAsync(
            "/branch/activation",
            new { code = generated!.ActivationCode });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        using (var scope = cloudFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var cloud = await db.CloudBranchInstances.AsNoTracking().SingleAsync();
            cloud.Status.Should().Be(CloudBranchInstance.ActiveStatus);
            cloud.TenantId.Should().Be(tenantId);
            cloud.BranchId.Should().Be(businessBranchId);
        }

        using (var scope = branchFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var local = await db.BranchInstances.AsNoTracking().SingleAsync();
            local.Status.Should().Be(BranchInstance.ActiveStatus);
            local.TenantId.Should().Be(tenantId);
            local.BranchId.Should().Be(businessBranchId);
        }

        var activeHealth = await branchClient.GetFromJsonAsync<BranchHealthDto>("/health/branch");
        activeHealth!.Status.Should().Be("Active");
        activeHealth.TenantId.Should().Be(tenantId.ToString("D"));
        activeHealth.BranchId.Should().Be(businessBranchId.ToString("D"));

        var again = await branchClient.PostAsJsonAsync(
            "/branch/activation",
            new { code = generated.ActivationCode });
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        var alreadyActive = await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId = businessBranchId });
        alreadyActive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await alreadyActive.Content.ReadAsStringAsync()).Should().Contain("BRANCH_ALREADY_ACTIVE");

        // Distinct installation cannot exchange onto the same Active business branch.
        var secondInstanceId = Guid.CreateVersion7();
        var secondKey = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            using (var scope = cloudFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
                var openCode = BranchActivationCode.Generate();
                db.BranchActivations.Add(BranchActivation.CreateOpen(
                    Guid.CreateVersion7(),
                    tenantId,
                    businessBranchId,
                    BranchActivationCode.Hash(openCode, "integration-test-cloud-activation-pepper-32b"),
                    DateTimeOffset.UtcNow.AddMinutes(20),
                    userId,
                    DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();

                var tokenHash = InstallationToken.Hash(InstallationToken.Generate());
                var signed = await SignChallengeAsync(cloudClient, secondInstanceId, secondKey, tokenHash);
                var denied = await cloudClient.PostAsJsonAsync(
                    "/cloud/branch-activations/exchange",
                    new
                    {
                        code = openCode,
                        branchInstanceId = secondInstanceId,
                        publicKey = secondKey.PublicKey,
                        challengeId = signed.ChallengeId,
                        signature = signed.Signature,
                        installationTokenHash = tokenHash,
                    });
                denied.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await denied.Content.ReadAsStringAsync()).Should().Contain("BRANCH_ALREADY_ACTIVE");
            }
        }
        finally
        {
            CryptographicOperationsZero(secondKey.PrivateKeyPkcs8);
        }

        (await cloudClient.PostAsJsonAsync("/branch/activation", new { code = "BNX-AAAAA-BBBBB" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await branchClient.PostAsJsonAsync("/cloud/branch-activations", new { branchId = businessBranchId }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_resume_keeps_single_receipt_token_hash_stable()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();
        await using var cloudFactory = CreateCloudFactory();
        using var cloudClient = cloudFactory.CreateClient();
        cloudClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, userId));

        var generate = await (await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId })).Content.ReadFromJsonAsync<GenerateDto>();

        var branchInstanceId = Guid.CreateVersion7();
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var installationToken = InstallationToken.Generate();
            var tokenHash = InstallationToken.Hash(installationToken);
            var first = await ExchangeAsync(cloudClient, generate!.ActivationCode, branchInstanceId, keyPair, tokenHash);
            var receiptA = first.Receipt;

            string? receiptB = null;
            string? receiptC = null;
            await Task.WhenAll(
                Task.Run(async () => receiptB = (await ResumeAsync(cloudClient, first.ActivationId, branchInstanceId, keyPair, tokenHash)).Receipt),
                Task.Run(async () => receiptC = (await ResumeAsync(cloudClient, first.ActivationId, branchInstanceId, keyPair, tokenHash)).Receipt));

            var produced = new[] { receiptB, receiptC }.Where(r => r is not null).Cast<string>().Distinct().ToList();
            produced.Should().NotBeEmpty();
            produced.Should().NotContain(receiptA);

            using var scope = cloudFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var activation = await db.BranchActivations.AsNoTracking().SingleAsync(x => x.Id == first.ActivationId);
            activation.InstallationTokenHash.Should().Be(tokenHash);
            activation.PublicKeyFingerprint.Should().Be(EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey));
            activation.AdoptedBranchInstanceId.Should().Be(branchInstanceId);

            var liveReceipt = produced.Single(r => InstallationToken.Hash(r) == activation.ActivationReceiptHash);
            foreach (var stale in produced.Where(r => r != liveReceipt).Append(receiptA))
            {
                (await ConfirmAsync(cloudClient, first.ActivationId, stale, installationToken))
                    .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            (await ConfirmAsync(cloudClient, first.ActivationId, liveReceipt, installationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var otherKey = EcdsaP256ActivationCrypto.GenerateKeyPair();
            try
            {
                var deny = await ResumeRawAsync(cloudClient, first.ActivationId, branchInstanceId, otherKey, tokenHash);
                deny.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var denyHash = await ResumeRawAsync(
                    cloudClient,
                    first.ActivationId,
                    branchInstanceId,
                    keyPair,
                    InstallationToken.Hash(InstallationToken.Generate()));
                denyHash.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
            finally
            {
                CryptographicOperationsZero(otherKey.PrivateKeyPkcs8);
            }
        }
        finally
        {
            CryptographicOperationsZero(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public async Task Human_code_alone_cannot_resume_after_reserved_without_pop()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();
        await using var cloudFactory = CreateCloudFactory();
        using var cloudClient = cloudFactory.CreateClient();
        cloudClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, userId));
        var generate = await (await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId })).Content.ReadFromJsonAsync<GenerateDto>();

        var branchInstanceId = Guid.CreateVersion7();
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var tokenHash = InstallationToken.Hash(InstallationToken.Generate());
            await ExchangeAsync(cloudClient, generate!.ActivationCode, branchInstanceId, keyPair, tokenHash);

            var bare = await cloudClient.PostAsJsonAsync(
                "/cloud/branch-activations/exchange",
                new
                {
                    code = generate.ActivationCode,
                    branchInstanceId,
                    publicKey = keyPair.PublicKey,
                    challengeId = Guid.CreateVersion7(),
                    signature = "not-a-signature",
                    installationTokenHash = tokenHash,
                });
            bare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            CryptographicOperationsZero(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public async Task Confirm_failure_matrix_keeps_ready_until_cloud_active_then_finalize()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();
        await using var cloudFactory = CreateCloudFactory();
        using var cloudClient = cloudFactory.CreateClient();
        cloudClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, userId));
        var generate = await (await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId })).Content.ReadFromJsonAsync<GenerateDto>();

        await using var branchFactory = CreateBranchFactoryWiredTo(
            cloudFactory,
            confirmFailuresBeforeSuccess: 1);
        using var branchClient = branchFactory.CreateClient();

        // First activate: confirm fails once → Branch stays Ready, Cloud stays Reserved/not Active
        var first = await branchClient.PostAsJsonAsync(
            "/branch/activation",
            new { code = generate!.ActivationCode });
        first.IsSuccessStatusCode.Should().BeFalse();

        using (var scope = branchFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            (await db.BranchInstances.AsNoTracking().SingleAsync()).Status
                .Should().Be(BranchInstance.ReadyForActivationStatus);
            var store = scope.ServiceProvider.GetRequiredService<IBranchCredentialStore>();
            (await store.GetSessionAsync()).Should().NotBeNull();
        }

        using (var scope = cloudFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            (await db.CloudBranchInstances.AsNoTracking().SingleAsync()).Status
                .Should().Be(CloudBranchInstance.ActivatingStatus);
        }

        // Finalize / retry succeeds without new materials
        var finalize = await branchClient.PostAsync("/branch/activation/finalize", null);
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, await finalize.Content.ReadAsStringAsync());

        using (var scope = branchFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var local = await db.BranchInstances.AsNoTracking().SingleAsync();
            local.Status.Should().Be(BranchInstance.ActiveStatus);
            var store = scope.ServiceProvider.GetRequiredService<IBranchCredentialStore>();
            (await store.GetSessionAsync()).Should().BeNull();
            (await store.GetPermanentAsync()).Should().NotBeNull();
        }

        // Idempotent local retry after Active
        (await branchClient.PostAsync("/branch/activation/finalize", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await branchClient.PostAsJsonAsync("/branch/activation", new { code = generate.ActivationCode }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private WebApplicationFactory<Program> CreateBranchFactoryWiredTo(
        WebApplicationFactory<Program> cloudFactory,
        int confirmFailuresBeforeSuccess = 0)
    {
        var remainingFailures = confirmFailuresBeforeSuccess;
        return new BranchActivationTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICloudActivationClient>();
                services.AddHttpClient<ICloudActivationClient, CloudActivationHttpClient>()
                    .ConfigurePrimaryHttpMessageHandler(() =>
                    {
                        HttpMessageHandler inner = cloudFactory.Server.CreateHandler();
                        if (remainingFailures > 0)
                        {
                            inner = new FailConfirmHandler(inner, () =>
                            {
                                if (remainingFailures > 0)
                                {
                                    remainingFailures--;
                                    return true;
                                }

                                return false;
                            });
                        }

                        return inner;
                    })
                    .ConfigureHttpClient(client => client.BaseAddress = new Uri("http://cloud.invalid"));
            });
        });
    }

    private WebApplicationFactory<Program> CreateCloudFactory() =>
        new BranchActivationTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("CloudActivation:CodePepper", "integration-test-cloud-activation-pepper-32b");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> SeedTenantBranchAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        db.Set<Tenant>().Add(new Tenant(tenantId, tenantId.ToString("N"), "E2E Tenant", DateTimeOffset.UtcNow));
        db.Set<Branch>().Add(new Branch(branchId, tenantId, "E2E Branch"));
        db.Set<User>().Add(new User(
            userId,
            tenantId,
            "e2e-admin@example.com",
            "E2E-ADMIN@EXAMPLE.COM",
            "hash",
            "ADMIN",
            branchId));
        await db.SaveChangesAsync();
        return (tenantId, branchId, userId);
    }

    private async Task ResetAsync()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE branch_activation_challenges, branch_activations, cloud_branch_instances, branch_instances CASCADE;
            """);
    }

    private static string CreateAdminJwt(Guid tenantId, Guid userId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("integration-test-signing-key-with-more-than-32-bytes"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "binexus",
            audience: "binexus-api",
            claims:
            [
                new Claim("sub", userId.ToString("D")),
                new Claim("tenantId", tenantId.ToString("D")),
                new Claim("role", "ADMIN"),
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(Guid ChallengeId, string Signature)> SignChallengeAsync(
        HttpClient cloudClient,
        Guid branchInstanceId,
        ActivationKeyPair keyPair,
        string tokenHash)
    {
        var challengeResponse = await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations/challenges",
            new { branchInstanceId, publicKey = keyPair.PublicKey, installationTokenHash = tokenHash });
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeDto>();
        var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey);
        var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
            challenge!.ChallengeId,
            branchInstanceId,
            fingerprint,
            tokenHash,
            challenge.Nonce,
            challenge.ExpiresAtUtc));
        return (challenge.ChallengeId, EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8));
    }

    private static async Task<ExchangeDto> ExchangeAsync(
        HttpClient cloudClient,
        string code,
        Guid branchInstanceId,
        ActivationKeyPair keyPair,
        string tokenHash)
    {
        var signed = await SignChallengeAsync(cloudClient, branchInstanceId, keyPair, tokenHash);
        var response = await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations/exchange",
            new
            {
                code,
                branchInstanceId,
                publicKey = keyPair.PublicKey,
                challengeId = signed.ChallengeId,
                signature = signed.Signature,
                installationTokenHash = tokenHash,
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExchangeDto>())!;
    }

    private static async Task<ExchangeDto> ResumeAsync(
        HttpClient cloudClient,
        Guid activationId,
        Guid branchInstanceId,
        ActivationKeyPair keyPair,
        string tokenHash)
    {
        var response = await ResumeRawAsync(cloudClient, activationId, branchInstanceId, keyPair, tokenHash);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExchangeDto>())!;
    }

    private static async Task<HttpResponseMessage> ResumeRawAsync(
        HttpClient cloudClient,
        Guid activationId,
        Guid branchInstanceId,
        ActivationKeyPair keyPair,
        string tokenHash)
    {
        var signed = await SignChallengeAsync(cloudClient, branchInstanceId, keyPair, tokenHash);
        return await cloudClient.PostAsJsonAsync(
            $"/cloud/branch-activations/{activationId:D}/resume",
            new
            {
                branchInstanceId,
                publicKey = keyPair.PublicKey,
                challengeId = signed.ChallengeId,
                signature = signed.Signature,
                installationTokenHash = tokenHash,
            });
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient cloudClient,
        Guid activationId,
        string receipt,
        string installationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/cloud/branch-activations/confirm")
        {
            Content = JsonContent.Create(new { activationId, receipt }),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Branch {installationToken}");
        return await cloudClient.SendAsync(request);
    }

    private static void CryptographicOperationsZero(byte[] buffer) =>
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);

    private sealed class BranchActivationTestFactory(Action<IWebHostBuilder> configure)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }

    private sealed class FailConfirmHandler(HttpMessageHandler inner, Func<bool> shouldFail) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/confirm", StringComparison.Ordinal) == true
                && shouldFail())
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = JsonContent.Create(new { code = "ACTIVATION_INVALID", detail = "forced confirm failure" }),
                };
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed record GenerateDto(
        [property: JsonPropertyName("activationId")] Guid ActivationId,
        [property: JsonPropertyName("activationCode")] string ActivationCode);

    private sealed record ChallengeDto(
        [property: JsonPropertyName("challengeId")] Guid ChallengeId,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);

    private sealed record ExchangeDto(
        [property: JsonPropertyName("activationId")] Guid ActivationId,
        [property: JsonPropertyName("receipt")] string Receipt);

    private sealed record BranchHealthDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("branchInstanceId")] string BranchInstanceId,
        [property: JsonPropertyName("tenantId")] string? TenantId,
        [property: JsonPropertyName("branchId")] string? BranchId);
}
