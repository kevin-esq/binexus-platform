using System.IdentityModel.Tokens.Jwt;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using FluentAssertions;

namespace Binexus.UnitTests.Identity;

public sealed class IdentitySecurityTests
{
    private const string NestHash =
        "$argon2id$v=19$m=65536,t=3,p=4$I/ldkJakfKcsF6SV2wB6Zg$aM2RBvTpjCf0yNFfhnE5Cy3juLgzptW87n9vmhHiIik";

    [Theory]
    [InlineData("  Admin@Acme.Test  ", "ADMIN@ACME.TEST")]
    [InlineData("admin@acme.test", "ADMIN@ACME.TEST")]
    [InlineData("u\u0308ser@example.test", "ÜSER@EXAMPLE.TEST")]
    public void Email_normalization_is_trimmed_unicode_normalized_and_case_insensitive(
        string input,
        string expected)
    {
        EmailNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public async Task Argon2_verifies_hash_created_by_nest()
    {
        var hasher = new Argon2PasswordHasher();

        (await hasher.VerifyAsync(NestHash, IdentitySeedDefaults.KnownInsecureDemoPassword)).Should().BeTrue();
        (await hasher.VerifyAsync(NestHash, "incorrect")).Should().BeFalse();
        hasher.NeedsRehash(NestHash).Should().BeFalse();
    }

    [Fact]
    public async Task Argon2_hashes_and_verifies_with_locked_parameters()
    {
        var hasher = new Argon2PasswordHasher();
        var hash = await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword);

        hash.Should().StartWith("$argon2id$v=19$m=65536,t=3,p=4$");
        (await hasher.VerifyAsync(hash, IdentitySeedDefaults.KnownInsecureDemoPassword)).Should().BeTrue();
        (await hasher.VerifyAsync(hash, "wrong")).Should().BeFalse();
    }

    [Fact]
    public async Task Argon2_rejects_corrupt_and_disallowed_variants()
    {
        var hasher = new Argon2PasswordHasher();
        (await hasher.VerifyAsync("not-a-hash", "password")).Should().BeFalse();
        (await hasher.VerifyAsync(
            "$argon2i$v=19$m=65536,t=3,p=4$I/ldkJakfKcsF6SV2wB6Zg$aM2RBvTpjCf0yNFfhnE5Cy3juLgzptW87n9vmhHiIik",
            IdentitySeedDefaults.KnownInsecureDemoPassword)).Should().BeFalse();
    }

    [Fact]
    public async Task Argon2_rejects_hostile_embedded_parameters()
    {
        var hostile =
            "$argon2id$v=19$m=1048576,t=50,p=16$I/ldkJakfKcsF6SV2wB6Zg$aM2RBvTpjCf0yNFfhnE5Cy3juLgzptW87n9vmhHiIik";
        Argon2PasswordHasher.TryValidateStoredParameters(hostile, out _).Should().BeFalse();
        (await new Argon2PasswordHasher().VerifyAsync(hostile, "x")).Should().BeFalse();
    }

    [Fact]
    public void Weak_parameters_require_rehash()
    {
        var weak =
            "$argon2id$v=19$m=8192,t=1,p=1$I/ldkJakfKcsF6SV2wB6Zg$aM2RBvTpjCf0yNFfhnE5Cy3juLgzptW87n9vmhHiIik";
        new Argon2PasswordHasher().NeedsRehash(weak).Should().BeTrue();
    }

    [Fact]
    public async Task Password_length_is_bounded_before_argon2()
    {
        var hasher = new Argon2PasswordHasher();
        var tooLong = new string('a', IPasswordHasher.MaxPasswordUtf8Bytes + 1);
        await FluentActions.Awaiting(() => hasher.HashAsync(tooLong))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => hasher.HashAsync(string.Empty))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Argon2_respects_cancellation()
    {
        var hasher = new Argon2PasswordHasher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await FluentActions.Awaiting(() => hasher.HashAsync("password", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Refresh_tokens_are_url_safe_random_and_sha256_hashed()
    {
        var tokens = Enumerable.Range(0, 128).Select(_ => RefreshTokenHasher.Generate()).ToArray();

        tokens.Should().OnlyHaveUniqueItems();
        tokens.Should().OnlyContain(token =>
            token.Length >= 43
            && !token.Contains('+', StringComparison.Ordinal)
            && !token.Contains('/', StringComparison.Ordinal)
            && !token.Contains('=', StringComparison.Ordinal));
        RefreshTokenHasher.Hash("token").Should().Be(
            "3c469e9d6c5875d37a43f353d4f88e61fcf812c66eee3457465a40b0da4153e0");
        RefreshTokenHasher.FixedTimeEqualsHex(
            RefreshTokenHasher.Hash("a"),
            RefreshTokenHasher.Hash("a")).Should().BeTrue();
    }

    [Fact]
    public void Access_token_contains_the_frontend_compatible_claims()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var options = new JwtOptions
        {
            Issuer = "binexus",
            Audience = "binexus-api",
            SigningKey = "test-signing-key-with-at-least-thirty-two-bytes",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(7),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var issuer = new JwtTokenIssuer(options, new FixedTimeProvider(now));

        var encoded = issuer.Issue(new AccessTokenSubject(
            userId,
            tenantId,
            "SUPER_ADMIN",
            branchId));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);

        token.Subject.Should().Be(userId.ToString());
        token.Issuer.Should().Be("binexus");
        token.Audiences.Should().ContainSingle("binexus-api");
        token.Claims.Single(x => x.Type == "tenantId").Value.Should().Be(tenantId.ToString());
        token.Claims.Single(x => x.Type == "role").Value.Should().Be("SUPER_ADMIN");
        token.Claims.Single(x => x.Type == "branchId").Value.Should().Be(branchId.ToString());
        token.Claims.Should().Contain(x => x.Type == JwtRegisteredClaimNames.Jti);
        token.ValidTo.Should().Be(now.AddMinutes(15).UtcDateTime);
        token.SignatureAlgorithm.Should().Be("HS256");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
