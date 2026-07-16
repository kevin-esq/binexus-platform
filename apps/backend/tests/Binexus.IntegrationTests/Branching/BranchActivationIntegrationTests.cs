using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Domain;
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
public sealed class BranchActivationIntegrationTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Generate_exchange_confirm_activates_cloud_before_local_publish_ready()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();

        await using var cloudFactory = CreateCloudFactory();
        var cloudClient = cloudFactory.CreateClient();
        var token = CreateAdminJwt(cloudFactory, tenantId, userId);
        cloudClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var generate = await cloudClient.PostAsJsonAsync(
            "/cloud/branch-activations",
            new { branchId });
        if (!generate.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)generate.StatusCode}: {await generate.Content.ReadAsStringAsync()}");
        }
        var generated = await generate.Content.ReadFromJsonAsync<GenerateDto>();
        generated.Should().NotBeNull();

        var branchInstanceId = Guid.CreateVersion7();
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var installationToken = InstallationToken.Generate();
            var tokenHash = InstallationToken.Hash(installationToken);
            var fingerprint = EcdsaP256ActivationCrypto.Fingerprint(keyPair.PublicKey);

            var challengeResponse = await cloudClient.PostAsJsonAsync(
                "/cloud/branch-activations/challenges",
                new { branchInstanceId, publicKey = keyPair.PublicKey, installationTokenHash = tokenHash });
            challengeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeDto>();

            var payload = CanonicalActivationChallengeCodec.Encode(new CanonicalActivationChallenge(
                challenge!.ChallengeId,
                branchInstanceId,
                fingerprint,
                tokenHash,
                challenge.Nonce,
                challenge.ExpiresAtUtc));
            var signature = EcdsaP256ActivationCrypto.Sign(payload, keyPair.PrivateKeyPkcs8);

            var exchangeResponse = await cloudClient.PostAsJsonAsync(
                "/cloud/branch-activations/exchange",
                new
                {
                    code = generated!.ActivationCode,
                    branchInstanceId,
                    publicKey = keyPair.PublicKey,
                    challengeId = challenge.ChallengeId,
                    signature,
                    installationTokenHash = tokenHash,
                });
            exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var exchange = await exchangeResponse.Content.ReadFromJsonAsync<ExchangeDto>();

            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
                var cloud = await db.CloudBranchInstances.SingleAsync(x => x.BranchInstanceId == branchInstanceId);
                cloud.Status.Should().Be(CloudBranchInstance.ActivatingStatus);
                var activation = await db.BranchActivations.SingleAsync(x => x.Id == exchange!.ActivationId);
                activation.Status.Should().Be(BranchActivation.ReservedStatus);
            }

            using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/cloud/branch-activations/confirm")
            {
                Content = JsonContent.Create(new { activationId = exchange!.ActivationId, receipt = exchange.Receipt }),
            };
            confirmRequest.Headers.TryAddWithoutValidation("Authorization", $"Branch {installationToken}");
            var confirmResponse = await cloudClient.SendAsync(confirmRequest);
            confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
                (await db.CloudBranchInstances.SingleAsync(x => x.BranchInstanceId == branchInstanceId))
                    .Status.Should().Be(CloudBranchInstance.ActiveStatus);
                (await db.BranchActivations.SingleAsync(x => x.Id == exchange.ActivationId))
                    .Status.Should().Be(BranchActivation.ConsumedStatus);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public async Task Exact_reserved_retry_rotates_receipt_not_token_hash()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();
        await using var cloudFactory = CreateCloudFactory();
        var cloudClient = cloudFactory.CreateClient();
        cloudClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(cloudFactory, tenantId, userId));

        var generated = await (await cloudClient.PostAsJsonAsync(
                "/cloud/branch-activations",
                new { branchId }))
            .Content.ReadFromJsonAsync<GenerateDto>();

        var branchInstanceId = Guid.CreateVersion7();
        var keyPair = EcdsaP256ActivationCrypto.GenerateKeyPair();
        try
        {
            var installationToken = InstallationToken.Generate();
            var tokenHash = InstallationToken.Hash(installationToken);
            var first = await ExchangeOnceAsync(
                cloudClient,
                generated!.ActivationCode,
                branchInstanceId,
                keyPair,
                tokenHash);
            var secondChallenge = await CreateSignedChallengeAsync(cloudClient, branchInstanceId, keyPair, tokenHash);
            var secondExchange = await cloudClient.PostAsJsonAsync(
                "/cloud/branch-activations/exchange",
                new
                {
                    code = generated.ActivationCode,
                    branchInstanceId,
                    publicKey = keyPair.PublicKey,
                    challengeId = secondChallenge.ChallengeId,
                    signature = secondChallenge.Signature,
                    installationTokenHash = tokenHash,
                });
            secondExchange.StatusCode.Should().Be(HttpStatusCode.OK);
            var second = await secondExchange.Content.ReadFromJsonAsync<ExchangeDto>();
            second!.Receipt.Should().NotBe(first.Receipt);

            using var scope = fixture.CreateScope();
            var activation = await scope.ServiceProvider.GetRequiredService<BinexusDbContext>()
                .BranchActivations.SingleAsync(x => x.Id == first.ActivationId);
            activation.InstallationTokenHash.Should().Be(tokenHash);
            activation.ActivationReceiptHash.Should().Be(InstallationToken.Hash(second.Receipt));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyPair.PrivateKeyPkcs8);
        }
    }

    [Fact]
    public async Task Cloud_endpoints_404_on_branch_branch_endpoints_absent_on_cloud()
    {
        await ResetAsync();

        await using var cloudFactory = CreateCloudFactory();
        (await cloudFactory.CreateClient().PostAsJsonAsync("/branch/activation", new { code = "BNX-AAAAA-AAAAA" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var branchFactory = CreateBranchFactory();
        (await branchFactory.CreateClient().PostAsJsonAsync("/cloud/branch-activations", new { branchId = Guid.CreateVersion7() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lazy_expiry_allows_generate_after_open_ttl()
    {
        await ResetAsync();
        var (tenantId, branchId, userId) = await SeedTenantBranchAsync();
        await using var cloudFactory = CreateCloudFactory(o =>
        {
            o.UseSetting("CloudActivation:CodeTtl", "00:00:01");
        });
        var client = cloudFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAdminJwt(cloudFactory, tenantId, userId));

        var first = await client.PostAsJsonAsync("/cloud/branch-activations", new { branchId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        var second = await client.PostAsJsonAsync("/cloud/branch-activations", new { branchId });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> SeedTenantBranchAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        db.Set<Tenant>().Add(new Tenant(tenantId, tenantId.ToString("N"), "Activation Tenant", DateTimeOffset.UtcNow));
        db.Set<Branch>().Add(new Branch(branchId, tenantId, "Main"));
        db.Set<User>().Add(new User(
            userId,
            tenantId,
            "admin@example.com",
            "ADMIN@EXAMPLE.COM",
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
            TRUNCATE TABLE branch_activation_challenges, branch_activations, cloud_branch_instances CASCADE;
            """);
    }

    private WebApplicationFactory<Program> CreateCloudFactory(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("CloudActivation:CodePepper", "integration-test-cloud-activation-pepper-32b");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            configure?.Invoke(builder);
        });

    private WebApplicationFactory<Program> CreateBranchFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("BranchCloud:BaseUrl", "http://127.0.0.1:5102");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

    private static string CreateAdminJwt(WebApplicationFactory<Program> factory, Guid tenantId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        // Issue a local HS256 token matching test SigningKey without going through AuthService.
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

    private static async Task<ExchangeDto> ExchangeOnceAsync(
        HttpClient cloudClient,
        string code,
        Guid branchInstanceId,
        ActivationKeyPair keyPair,
        string tokenHash)
    {
        var signed = await CreateSignedChallengeAsync(cloudClient, branchInstanceId, keyPair, tokenHash);
        var exchangeResponse = await cloudClient.PostAsJsonAsync(
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
        if (!exchangeResponse.IsSuccessStatusCode)
        {
            var body = await exchangeResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{(int)exchangeResponse.StatusCode}: {body}");
        }

        return (await exchangeResponse.Content.ReadFromJsonAsync<ExchangeDto>())!;
    }

    private static async Task<(Guid ChallengeId, string Signature)> CreateSignedChallengeAsync(
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
}
