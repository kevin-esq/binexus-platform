using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Identity;

public sealed class AuthFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<CloudApiFactory>
{
    private const string SigningKey = "identity-integration-signing-key-with-more-than-thirty-two-bytes";
    private readonly PostgresTestFixture _postgres;
    private readonly CloudApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(PostgresTestFixture postgres, CloudApiFactory factory)
    {
        _postgres = postgres;
        _factory = factory;
        _client = CreateClient(factory, postgres.ConnectionString);
    }

    private static HttpClient CreateClient(
        CloudApiFactory factory,
        string connectionString,
        string environment = "Testing") =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", environment);
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Jwt:Issuer", "binexus");
            builder.UseSetting("Jwt:Audience", "binexus-api");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenLifetime", "00:15:00");
            builder.UseSetting("Jwt:RefreshTokenLifetime", "7.00:00:00");
            builder.UseSetting("Jwt:ClockSkew", "00:00:30");
            builder.UseSetting("IdentitySeed:AdminPassword", IdentitySeedDefaults.KnownInsecureDemoPassword);
        }).CreateClient();

    [Fact]
    public async Task Login_refresh_logout_treat_refresh_token_as_opaque_string()
    {
        var login = await LoginAsync();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Split('.').Length.Should().NotBe(3, "refresh must not require JWT format");

        var refresh = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshRequest(login.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await refresh.Content.ReadFromJsonAsync<AuthTokens>())!;

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = JsonContent.Create(new RefreshRequest(rotated.RefreshToken)),
        };
        logoutRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        var logout = await _client.SendAsync(logoutRequest);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Login_and_me_match_the_existing_http_contract()
    {
        var tokens = await LoginAsync();

        var response = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", tokens.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<AuthSession>();

        session.Should().NotBeNull();
        session!.User.Email.Should().Be("admin@acme.test");
        session.User.Role.Should().Be(RoleNames.SuperAdmin);
        session.User.TenantId.Should().Be(session.Tenant.Id);
        session.Tenant.Slug.Should().Be("acme");
        session.Branch!.Name.Should().Be("Main");
    }

    [Theory]
    [InlineData("missing", "admin@acme.test", "ChangeMe123!")]
    [InlineData("acme", "missing@acme.test", "ChangeMe123!")]
    [InlineData("acme", "admin@acme.test", "wrong-password")]
    public async Task Login_uses_a_uniform_invalid_credentials_error(
        string tenantSlug,
        string email,
        string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(tenantSlug, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_disabled_user_returns_same_public_invalid_credentials()
    {
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(true);
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/auth/login",
                new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvalidCredentials);
        }
        finally
        {
            using var scope = _postgres.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(false);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Me_for_disabled_user_returns_account_unavailable()
    {
        var tokens = await LoginAsync();
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(true);
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", tokens.AccessToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.AccountUnavailable);
        }
        finally
        {
            using var scope = _postgres.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(false);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Refresh_for_disabled_user_returns_invalid_refresh_token()
    {
        var tokens = await LoginAsync();
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(true);
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/auth/refresh",
                new RefreshRequest(tokens.RefreshToken));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvalidRefreshToken);
        }
        finally
        {
            using var scope = _postgres.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var user = await db.Set<User>().IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .SingleAsync(x => x.NormalizedEmail == "ADMIN@ACME.TEST"
                    && x.Tenant.Slug == "acme");
            user.SetDisabled(false);
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Cashier)]
    [InlineData(RoleNames.Warehouse)]
    [InlineData(RoleNames.Driver)]
    public void Known_roles_are_accepted_by_catalog(string role) =>
        RoleNames.IsKnown(role).Should().BeTrue();

    [Fact]
    public async Task Database_rejects_unknown_role_via_check_constraint()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");

        await FluentActions.Awaiting(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, tenant_id, email, normalized_email, password_hash, role, is_system, is_disabled)
            VALUES ({ids.NewId()}, {tenant.Id}, 'bad-role@acme.test', 'BAD-ROLE@ACME.TEST', 'x', 'NOT_A_ROLE', false, false)
            """))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Refresh_with_unknown_role_returns_invalid_refresh_token()
    {
        var tokens = await LoginAsync();
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            // Bypass check constraint temporarily to simulate corrupted data.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE users DROP CONSTRAINT ck_users_role");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE users SET role = 'GOD_MODE'
                WHERE normalized_email = 'ADMIN@ACME.TEST'
                """);
        }

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/auth/refresh",
                new RefreshRequest(tokens.RefreshToken));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.InvalidRefreshToken);
        }
        finally
        {
            using var scope = _postgres.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE users SET role = {RoleNames.SuperAdmin}
                WHERE normalized_email = 'ADMIN@ACME.TEST'
                """);
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE users ADD CONSTRAINT ck_users_role
                CHECK (role IN ('SUPER_ADMIN','ADMIN','CASHIER','WAREHOUSE','DRIVER'))
                """);
        }
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_family()
    {
        var original = await LoginAsync();
        var rotatedResponse = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshRequest(original.RefreshToken));
        rotatedResponse.EnsureSuccessStatusCode();
        var rotated = (await rotatedResponse.Content.ReadFromJsonAsync<AuthTokens>())!;
        rotated.RefreshToken.Should().NotBe(original.RefreshToken);

        var reuseResponse = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshRequest(original.RefreshToken));
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(reuseResponse)).Should().Be(AuthErrorCodes.RefreshTokenReused);

        var replacementResponse = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshRequest(rotated.RefreshToken));
        replacementResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_one_concurrent_refresh_succeeds_and_leaves_no_extra_active_token()
    {
        var tokens = await LoginAsync();
        var originalHash = RefreshTokenHasher.Hash(tokens.RefreshToken);

        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokens.RefreshToken)),
            _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokens.RefreshToken)));

        responses.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
        var failure = responses.Single(x => x.StatusCode == HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(failure)).Should().Be(AuthErrorCodes.RefreshTokenReused);

        var winner = responses.Single(x => x.StatusCode == HttpStatusCode.OK);
        var winnerTokens = (await winner.Content.ReadFromJsonAsync<AuthTokens>())!;

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var familyId = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.TokenHash == originalHash)
            .Select(x => x.FamilyId)
            .SingleAsync();

        var active = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.FamilyId == familyId
                && x.RevokedAtUtc == null
                && x.UsedAtUtc == null
                && x.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToListAsync();

        // Concurrent loser triggers family revoke. That may invalidate the winner token too
        // (safe session kill on network retry). At most one active token may remain.
        active.Count.Should().BeLessThanOrEqualTo(1);
        if (active.Count == 1)
        {
            active[0].TokenHash.Should().Be(RefreshTokenHasher.Hash(winnerTokens.RefreshToken));
        }
        else
        {
            var followUp = await _client.PostAsJsonAsync(
                "/auth/refresh",
                new RefreshRequest(winnerTokens.RefreshToken));
            followUp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Logout_revokes_only_the_presented_refresh_token()
    {
        var tokens = await LoginAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = JsonContent.Create(new RefreshRequest(tokens.RefreshToken)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var logout = await _client.SendAsync(request);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(refresh)).Should().Be(AuthErrorCodes.RefreshTokenReused);
    }

    [Fact]
    public async Task Expired_access_token_returns_token_expired()
    {
        var session = await GetSeedSessionAsync();
        var token = CreateAccessToken(
            session.User.Id,
            session.Tenant.Id,
            issuer: "binexus",
            audience: "binexus-api",
            key: SigningKey,
            expires: DateTime.UtcNow.AddMinutes(-2));

        var response = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorCodeAsync(response)).Should().Be(AuthErrorCodes.TokenExpired);
    }

    [Theory]
    [InlineData("wrong-issuer", "binexus-api", SigningKey)]
    [InlineData("binexus", "wrong-audience", SigningKey)]
    [InlineData("binexus", "binexus-api", "different-signing-key-with-more-than-thirty-two-bytes")]
    public async Task Invalid_signature_issuer_or_audience_is_rejected(
        string issuer,
        string audience,
        string key)
    {
        var session = await GetSeedSessionAsync();
        var token = CreateAccessToken(
            session.User.Id,
            session.Tenant.Id,
            issuer,
            audience,
            key,
            DateTime.UtcNow.AddMinutes(5));

        var response = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_isolated_by_tenant_slug()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("other-tenant", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Normalized_email_is_unique_per_tenant_and_allowed_across_tenants()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id);

        db.Add(new User(
            ids.NewId(),
            tenant.Id,
            "  ADMIN@ACME.TEST ",
            EmailNormalizer.Normalize("  ADMIN@ACME.TEST "),
            "unused",
            RoleNames.Admin,
            branch.Id));
        await FluentActions.Awaiting(() => db.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();

        db.ChangeTracker.Clear();
        var otherTenant = new Tenant(ids.NewId(), "other-" + Guid.NewGuid().ToString("N")[..8], "Other", DateTimeOffset.UtcNow);
        var otherBranch = new Branch(ids.NewId(), otherTenant.Id, "Main");
        db.Add(otherTenant);
        db.Add(otherBranch);
        db.Add(new User(
            ids.NewId(),
            otherTenant.Id,
            "admin@acme.test",
            EmailNormalizer.Normalize("admin@acme.test"),
            "unused",
            RoleNames.Admin,
            otherBranch.Id));
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Admin@Acme.Test")]
    [InlineData("admin@acme.test")]
    [InlineData("  admin@acme.test  ")]
    public async Task Login_accepts_normalized_email_variants(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", email, IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_rate_limit_returns_too_many_requests_with_retry_after()
    {
        using var rateLimitedClient = CreateClient(_factory, _postgres.ConnectionString);
        HttpResponseMessage? response = null;
        for (var index = 0; index < 35 && response?.StatusCode != HttpStatusCode.TooManyRequests; index++)
        {
            response = await rateLimitedClient.PostAsJsonAsync("/auth/login", new
            {
                tenantSlug = string.Empty,
                email = string.Empty,
                password = string.Empty,
            });
        }

        response!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task Development_seed_is_idempotent()
    {
        using var scope = _postgres.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>();

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenantId = await db.Set<Tenant>().IgnoreQueryFilters()
            .Where(x => x.Slug == "acme")
            .Select(x => x.Id)
            .SingleAsync();
        (await db.Set<Tenant>().IgnoreQueryFilters().CountAsync(x => x.Slug == "acme")).Should().Be(1);
        (await db.Set<User>().IgnoreQueryFilters().CountAsync(
            x => x.TenantId == tenantId && x.NormalizedEmail == "ADMIN@ACME.TEST")).Should().Be(1);
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
    }

    private async Task<AuthSession> GetSeedSessionAsync()
    {
        var tokens = await LoginAsync();
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", tokens.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    private Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request);
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        if (document.RootElement.TryGetProperty("code", out var code))
        {
            return code.GetString()!;
        }

        return document.RootElement.GetProperty("title").GetString()!;
    }

    private static string CreateAccessToken(
        Guid userId,
        Guid tenantId,
        string issuer,
        string audience,
        string key,
        DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("tenantId", tenantId.ToString()),
                new Claim("role", RoleNames.SuperAdmin),
                new Claim("branchId", string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
