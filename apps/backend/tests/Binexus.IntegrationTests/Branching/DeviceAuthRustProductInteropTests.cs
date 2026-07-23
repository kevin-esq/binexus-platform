using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Binexus.IntegrationTests.Branching;

/// <summary>Verifies the product Rust DAT client against a live Kestrel Branch Runtime.</summary>
[Collection("postgres")]
public sealed class DeviceAuthRustProductInteropTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private const string SigningKey = "integration-test-signing-key-with-more-than-32-bytes";
    private const string Pepper = "integration-test-branch-pairing-pepper-0000";

    [Fact]
    public async Task Rust_product_client_uses_dat_for_operational_route_and_observes_revocation()
    {
        var pairingInterop = ResolveInteropExecutable("pairing_interop");
        var deviceAuthInterop = ResolveInteropExecutable("device_auth_interop");
        await using var context = await StartListeningBranchAsync();
        using var admin = context.CreateAdminClient();

        var pairing = await admin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        pairing.EnsureSuccessStatusCode();
        var pairingSession = (await pairing.Content.ReadFromJsonAsync<PairingSessionDto>())!;
        var dataDir = Path.Join(Path.GetTempPath(), "binexus-device-auth-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        using (var process = Start(pairingInterop, psi =>
        {
            psi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
            psi.Environment["BINEXUS_PAIRING_CODE"] = $"{pairingSession.PairingSessionId:D}:{pairingSession.PairingCode}";
            psi.Environment["BINEXUS_DATA_DIR"] = dataDir;
            psi.Environment["BINEXUS_MODE"] = "full";
            psi.Environment["BINEXUS_TERMINAL_NAME"] = "DAT Rust Interop";
            psi.Environment["BINEXUS_POLL_SECS"] = "90";
        }))
        {
            var exchanged = await ReadEventAsync(process.StandardOutput, "exchanged");
            var requestId = Guid.Parse(exchanged.GetProperty("pairingRequestId").GetString()!);
            (await admin.PostAsync($"/branch/pairing/requests/{requestId:D}/approve", null)).EnsureSuccessStatusCode();
            await ReadEventAsync(process.StandardOutput, "paired");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync());
        }

        using (var operational = Start(deviceAuthInterop, psi =>
        {
            psi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
            psi.Environment["BINEXUS_DATA_DIR"] = dataDir;
            psi.Environment["BINEXUS_MODE"] = "device-auth-full";
            psi.Environment["BINEXUS_USER_JWT"] = CreateUserJwt(context.TenantId, context.BranchId, context.UserId);
        }))
        {
            var operationalEvent = await ReadEventAsync(operational.StandardOutput, "operational");
            operationalEvent.GetProperty("status").GetInt32().Should().NotBe((int)HttpStatusCode.Unauthorized);
            await operational.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            operational.ExitCode.Should().Be(0, await operational.StandardError.ReadToEndAsync());
        }

        using var probe = Start(deviceAuthInterop, psi =>
        {
            psi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
            psi.Environment["BINEXUS_DATA_DIR"] = dataDir;
            psi.Environment["BINEXUS_MODE"] = "device-auth-revoke-probe";
            psi.Environment["BINEXUS_USER_JWT"] = CreateUserJwt(context.TenantId, context.BranchId, context.UserId);
        });

        var me = await ReadEventAsync(probe.StandardOutput, "me");
        var deviceId = Guid.Parse(me.GetProperty("deviceId").GetString()!);
        (await admin.PostAsync($"/branch/devices/{deviceId:D}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await probe.StandardInput.WriteLineAsync("REVOKE_DONE");
        await probe.StandardInput.FlushAsync();
        var revoked = await ReadEventAsync(probe.StandardOutput, "post_revoke");
        revoked.GetProperty("status").GetInt32().Should().Be(403);
        revoked.GetProperty("code").GetString().Should().Be("DEVICE_REVOKED");
        await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        probe.ExitCode.Should().Be(0, await probe.StandardError.ReadToEndAsync());
    }

    private static Process Start(string executable, Action<ProcessStartInfo> configure)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        configure(psi);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Rust interop process.");
        return process;
    }

    private static async Task<JsonElement> ReadEventAsync(StreamReader output, string expectedEvent)
    {
        while (await output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90)) is { } line)
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var eventName = root.GetProperty("event").GetString();
            if (eventName == "error")
            {
                throw new InvalidOperationException(line);
            }

            if (eventName == expectedEvent)
            {
                return root.Clone();
            }
        }

        throw new InvalidOperationException($"Rust interop ended before '{expectedEvent}'.");
    }

    private static string ResolveInteropExecutable(string name)
    {
        var root = FindRepoRoot();
        var path = new[]
        {
            Path.Join(root, "apps", "desktop", "src-tauri", "target", "debug", name + ".exe"),
            Path.Join(root, "apps", "desktop", "src-tauri", "target", "release", name + ".exe"),
        }.FirstOrDefault(File.Exists);
        return path ?? throw new FileNotFoundException($"Build {name} with cargo build --bin {name}.");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "pnpm-workspace.yaml")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private async Task<ListeningBranchContext> StartListeningBranchAsync()
    {
        await fixture.ApplyMigrationsAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                TRUNCATE TABLE device_auth_challenges, device_pairing_challenges, device_pairing_requests,
                    device_pairing_sessions, branch_devices, branch_terminals, branch_instances CASCADE;
                """);
            var instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            var tenantId = Guid.CreateVersion7();
            var branchId = Guid.CreateVersion7();
            var userId = Guid.CreateVersion7();
            instance.Activate(tenantId, branchId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();

            var factory = new ListeningBranchFactory(fixture.ConnectionString);
            factory.UseKestrel(0);
            factory.StartServer();
            var address = factory.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("Kestrel did not publish a listener.");
            return new ListeningBranchContext(factory, tenantId, branchId, userId, address);
        }
    }

    private static string CreateUserJwt(Guid tenantId, Guid branchId, Guid userId)
    {
        var token = new JwtSecurityToken(
            issuer: "binexus",
            audience: "binexus-api",
            claims: [new Claim("sub", userId.ToString("D")), new Claim("tenantId", tenantId.ToString("D")),
                new Claim("branchId", branchId.ToString("D")), new Claim("role", "ADMIN")],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class ListeningBranchContext(ListeningBranchFactory factory, Guid tenantId, Guid branchId, Guid userId, string baseAddress) : IAsyncDisposable
    {
        public string BaseAddress { get; } = baseAddress;
        public Guid TenantId { get; } = tenantId;
        public Guid BranchId { get; } = branchId;
        public Guid UserId { get; } = userId;
        public HttpClient CreateAdminClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateUserJwt(TenantId, BranchId, UserId));
            return client;
        }
        public async ValueTask DisposeAsync() => await factory.DisposeAsync();
    }

    private sealed class ListeningBranchFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", Pepper);
            builder.UseSetting("BranchDeviceAuth:CurrentKeyId", "test-dat-1");
            builder.UseSetting("BranchDeviceAuth:SigningKeys:0:KeyId", "test-dat-1");
            builder.UseSetting(
                "BranchDeviceAuth:SigningKeys:0:Key",
                "integration-test-branch-device-auth-signing-key-32b");
            builder.UseSetting("BranchDeviceAuth:AllowInsecureBranchTransport", "true");
            builder.UseSetting("BranchDeviceAuth:GlobalPermitLimit", "5000");
            builder.UseSetting("BranchDeviceAuth:IpPermitLimit", "1000");
            builder.UseSetting("BranchDeviceAuth:DevicePermitLimit", "1000");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseEnvironment("Testing");
        }
    }

    private sealed record PairingSessionDto(Guid PairingSessionId, string PairingCode);
}
