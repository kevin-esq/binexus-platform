using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Binexus.Composition;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Binexus.Spike.BranchGuiSmokeHost;

/// <summary>
/// Long-lived Branch Runtime (Kestrel) + PostgreSQL for PR5 GUI smoke.
/// Writes host info to %TEMP%\binexus-gui-smoke-host.json.
/// </summary>
public static class HostRunner
{
    private const string SigningKey = "integration-test-signing-key-with-more-than-32-bytes";
    private const string Pepper = "integration-test-branch-pairing-pepper-0000";

    public static async Task<int> Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5102;
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("binexus_smoke")
            .WithUsername("binexus")
            .WithPassword("binexus")
            .Build();
        await postgres.StartAsync();

        await using var scopeFactory = new HostServiceFactory(postgres.GetConnectionString());
        await scopeFactory.MigrateAndSeedIdentityAsync();

        var tenantId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        Guid branchInstanceId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            instance.Activate(tenantId, branchId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();
            branchInstanceId = instance.Id;
        }

        await using var factory = new ListeningBranchFactory(
            postgres.GetConnectionString(),
            SigningKey,
            Pepper,
            $"http://127.0.0.1:{port}");
        _ = factory.CreateClient();
        var baseUrl = factory.ServerAddress.TrimEnd('/');
        var adminJwt = CreateAdminJwt(tenantId, branchId, userId);

        var infoPath = Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-host.json");
        await File.WriteAllTextAsync(
            infoPath,
            JsonSerializer.Serialize(new
            {
                baseUrl,
                branchInstanceId = branchInstanceId.ToString("D"),
                branchInstanceIdShort = branchInstanceId.ToString("D")[..8],
                tenantId = tenantId.ToString("D"),
                branchId = branchId.ToString("D"),
                adminJwt,
                infoPath,
            }));

        Console.WriteLine($"BINEXUS_SMOKE_HOST_READY baseUrl={baseUrl} instance={branchInstanceId.ToString("D")[..8]} info={infoPath}");
        var stopPath = Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-host.stop");
        var discardPath = Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-discard-receipt.txt");
        var expirePath = Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-expire-request.txt");
        File.Delete(stopPath);
        File.Delete(discardPath);
        File.Delete(expirePath);
        Console.WriteLine($"Waiting for stop file: {stopPath}");
        Console.WriteLine($"Smoke hooks: discard={discardPath} expire={expirePath}");
        while (!File.Exists(stopPath))
        {
            await TrySmokeDiscardReceiptAsync(factory, discardPath);
            await TrySmokeExpireRequestAsync(factory, expirePath);
            await Task.Delay(500);
        }

        return 0;
    }

    /// <summary>
    /// Documented reissue prep: drop the one-shot InMemory receipt so the next status poll
    /// returns Approved without a raw receipt (same effect as losing the vault after approve).
    /// </summary>
    private static Task TrySmokeDiscardReceiptAsync(ListeningBranchFactory factory, string path)
    {
        if (!File.Exists(path))
        {
            return Task.CompletedTask;
        }

        try
        {
            var raw = File.ReadAllText(path).Trim();
            File.Delete(path);
            if (!Guid.TryParse(raw, out var requestId))
            {
                Console.WriteLine("SMOKE_DISCARD_BAD_ID");
                return Task.CompletedTask;
            }

            factory.KestrelServices.GetRequiredService<IPairingReceiptVault>().Discard(requestId);
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-discard-receipt.ack"),
                "DISCARD_OK");
            Console.WriteLine($"SMOKE_DISCARD_OK request={requestId.ToString("D")[..8]}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMOKE_DISCARD_ERR {ex.GetType().Name}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Force request expiry before approve (scenario D) without waiting for RequestTtl.
    /// </summary>
    private static async Task TrySmokeExpireRequestAsync(ListeningBranchFactory factory, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var raw = File.ReadAllText(path).Trim();
            File.Delete(path);
            if (!Guid.TryParse(raw, out var requestId))
            {
                Console.WriteLine("SMOKE_EXPIRE_BAD_ID");
                return;
            }

            using var scope = factory.KestrelServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var vault = scope.ServiceProvider.GetRequiredService<IPairingReceiptVault>();
            var request = await db.DevicePairingRequests.SingleOrDefaultAsync(x => x.Id == requestId);
            if (request is null)
            {
                Console.WriteLine("SMOKE_EXPIRE_NOT_FOUND");
                return;
            }

            request.MarkExpired();
            vault.Discard(requestId);
            await db.SaveChangesAsync();
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "binexus-gui-smoke-expire-request.ack"),
                $"EXPIRE_OK:{request.Status}");
            Console.WriteLine($"SMOKE_EXPIRE_OK request={requestId.ToString("D")[..8]} status={request.Status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMOKE_EXPIRE_ERR {ex.GetType().Name}");
        }
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
            expires: DateTime.UtcNow.AddHours(4),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class HostServiceFactory(string connectionString) : IAsyncDisposable
    {
        private ServiceProvider? _provider;

        public async Task MigrateAndSeedIdentityAsync()
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>().SeedAsync();
        }

        public IServiceScope CreateScope()
        {
            _provider ??= Build();
            return _provider.CreateScope();
        }

        private ServiceProvider Build()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                    ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                    ["ASPNETCORE_ENVIRONMENT"] = "Testing",
                    ["Binexus:RuntimeMode"] = "Cloud",
                    ["Jwt:Issuer"] = "binexus",
                    ["Jwt:Audience"] = "binexus-api",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:AccessTokenLifetime"] = "00:15:00",
                    ["Jwt:RefreshTokenLifetime"] = "7.00:00:00",
                    ["Jwt:ClockSkew"] = "00:00:30",
                    ["IdentitySeed:AdminPassword"] = IdentitySeedDefaults.KnownInsecureDemoPassword,
                })
                .Build();
            var env = new SmokeHostEnvironment("Testing");
            services.AddSingleton<IHostEnvironment>(env);
            services.AddBinexusCore(configuration, env);
            services.AddBinexusRuntime(configuration);
            services.AddLogging();
            return services.BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            if (_provider is not null)
            {
                await _provider.DisposeAsync();
            }
        }

        private sealed class SmokeHostEnvironment(string environmentName) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = environmentName;
            public string ApplicationName { get; set; } = "BranchGuiSmokeHost";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }

    private sealed class ListeningBranchFactory(
        string connectionString,
        string signingKey,
        string pepper,
        string urls) : WebApplicationFactory<Program>
    {
        private IHost? _kestrelHost;
        public string ServerAddress { get; private set; } = urls;
        public IServiceProvider KestrelServices =>
            _kestrelHost?.Services
            ?? throw new InvalidOperationException("Kestrel host not started");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var apiContentRoot = ResolveApiContentRoot();
            builder.UseContentRoot(apiContentRoot);
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Jwt:SigningKey", signingKey);
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", pepper);
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.UseKestrel();
            builder.UseUrls(urls);
        }

        private static string ResolveApiContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Binexus.Api");
                if (File.Exists(Path.Combine(candidate, "Binexus.Api.csproj")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate apps/backend/src/Binexus.Api content root.");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var testHost = builder.Build();
            builder.ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls(urls);
            });
            _kestrelHost = builder.Build();
            _kestrelHost.Start();
            var server = _kestrelHost.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("IServerAddressesFeature missing");
            ServerAddress = addresses.Addresses.First();
            return testHost;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _kestrelHost?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
