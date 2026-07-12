using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Logistics;

[Collection("postgres")]
public sealed class LocalDevObjectStorageTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "local-dev-storage-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public LocalDevObjectStorageTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.UseSetting("Database:ConnectionString", postgres.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "binexus");
            builder.UseSetting("Jwt:Audience", "binexus-api");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenLifetime", "00:15:00");
            builder.UseSetting("Jwt:RefreshTokenLifetime", "7.00:00:00");
            builder.UseSetting("Jwt:ClockSkew", "00:00:30");
            builder.UseSetting("IdentitySeed:AdminPassword", IdentitySeedDefaults.KnownInsecureDemoPassword);
            builder.UseSetting("Features:LiquidationKillSwitch", "true");
            builder.UseSetting("Logistics:Storage:Provider", "Local");
            builder.UseSetting("Logistics:Storage:Endpoint", "http://localhost");
            builder.UseSetting("Logistics:Storage:MaxProofBytes", "1024");
        }).CreateClient();
    }

    [Fact]
    public async Task Put_rejects_traversal_unissued_wrong_type_and_overwrite_after_issued_upload()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId);
        await SeedCandidateAsync(context.TenantId, context.BranchId, orderId);

        var route = await PostJsonAsync<DeliveryRouteSummary>(
            "/logistics/delivery-routes",
            new CreateDeliveryRouteRequest(context.BranchId, null),
            tokens.AccessToken,
            $"route-{Guid.NewGuid():N}");
        await PostOkAsync(
            $"/logistics/delivery-routes/{route.Id}/assign-orders",
            new AssignOrdersRequest([orderId]),
            tokens.AccessToken,
            $"assign-{Guid.NewGuid():N}");
        await PostOkAsync(
            $"/logistics/delivery-routes/{route.Id}/dispatch",
            new DispatchDeliveryRouteRequest(context.UserId),
            tokens.AccessToken,
            $"dispatch-{Guid.NewGuid():N}");

        var stops = await GetJsonAsync<ListDeliveryRouteStopsResult>(
            $"/logistics/delivery-routes/{route.Id}/stops",
            tokens.AccessToken);
        var stopId = stops.Items[0].Id;

        var traversal = await _client.PutAsync(
            "/internal/dev-object-storage/tenants/../etc/passwd",
            new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } });
        traversal.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unissued = await _client.PutAsync(
            $"/internal/dev-object-storage/tenants/{context.TenantId:D}/delivery-proofs/{stopId:D}/photo-missing.jpg",
            new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } });
        unissued.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var upload = await PostJsonAsync<DeliveryProofUploadResult>(
            $"/logistics/delivery-route-stops/{stopId}/proof-uploads",
            new CreateDeliveryProofUploadRequest("PHOTO", "image/jpeg", 64),
            tokens.AccessToken,
            $"proof-{Guid.NewGuid():N}");

        var wrongType = new ByteArrayContent([1, 2, 3, 4]);
        wrongType.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var wrongTypeResponse = await _client.PutAsync(upload.UploadUrl.PathAndQuery, wrongType);
        wrongTypeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', 64)));
        body.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var first = await _client.PutAsync(upload.UploadUrl.PathAndQuery, body);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondBody = new ByteArrayContent(Encoding.UTF8.GetBytes(new string('y', 64)));
        secondBody.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var second = await _client.PutAsync(upload.UploadUrl.PathAndQuery, secondBody);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task PostOkAsync(string url, object body, string token, string key)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body, token, key);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<T> PostJsonAsync<T>(string url, object body, string token, string key)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body, token, key);
        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> GetJsonAsync<T>(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, object body, string token, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedCandidateAsync(Guid tenantId, Guid branchId, Guid orderId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new DeliveryRouteCandidate(ids.NewId(), tenantId, orderId, branchId, ids.NewId(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedOrderAsync(Guid tenantId, Guid branchId, Guid actorId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var orderId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new Order(
            orderId,
            tenantId,
            branchId,
            $"cust-{Guid.NewGuid():N}",
            "USD",
            "CARD",
            actorId,
            ids.NewId(),
            [new OrderLine(ids.NewId(), orderId, $"sku-{Guid.NewGuid():N}", "Widget", 1, 100)],
            now);
        db.Add(order);
        db.Add(order.Approve(ids.NewId(), actorId, null, now));
        db.Add(order.MoveToPicking(ids.NewId(), actorId, null, now));
        db.Add(order.MarkReadyForDeliveryRoute(ids.NewId(), actorId, null, now));
        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> AcmeContextAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        return (tenant.Id, branch.Id, user.Id);
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }
}
