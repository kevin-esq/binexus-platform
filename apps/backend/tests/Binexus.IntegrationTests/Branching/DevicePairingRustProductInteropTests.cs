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

/// <summary>
/// Full protocol interop: Rust product client (<c>pairing_interop</c>) → Branch Runtime (Kestrel) → PostgreSQL.
/// Required by PR5 desktop gate; not golden-vector-only.
/// </summary>
[Collection("postgres")]
public sealed class DevicePairingRustProductInteropTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private const string SigningKey = "integration-test-signing-key-with-more-than-32-bytes";
    private const string Pepper = "integration-test-branch-pairing-pepper-0000";

    [Fact]
    public async Task Rust_product_client_completes_full_ceremony_against_branch_runtime_and_postgres()
    {
        var interopExe = ResolveInteropExecutable();
        await using var context = await StartListeningBranchAsync();
        using var admin = context.CreateAdminClient();

        var sessionResponse = await admin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<SessionDto>())!;

        var dataDir = Path.Join(Path.GetTempPath(), "binexus-rust-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var pairingCode = $"{session.PairingSessionId:D}:{session.PairingCode}";
        var psi = new ProcessStartInfo
        {
            FileName = interopExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
        psi.Environment["BINEXUS_PAIRING_CODE"] = pairingCode;
        psi.Environment["BINEXUS_DATA_DIR"] = dataDir;
        psi.Environment["BINEXUS_MODE"] = "full";
        psi.Environment["BINEXUS_TERMINAL_NAME"] = "Rust Interop Terminal";
        psi.Environment["BINEXUS_POLL_SECS"] = "90";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exchanged = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var ev))
            {
                return;
            }

            switch (ev.GetString())
            {
                case "exchanged":
                    exchanged.TrySetResult(Guid.Parse(root.GetProperty("pairingRequestId").GetString()!));
                    break;
                case "paired":
                    paired.TrySetResult();
                    break;
                case "error":
                    exchanged.TrySetException(new InvalidOperationException(e.Data));
                    paired.TrySetException(new InvalidOperationException(e.Data));
                    break;
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start().Should().BeTrue();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var requestId = await exchanged.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var approve = await admin.PostAsync($"/branch/pairing/requests/{requestId:D}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        await paired.Task.WaitAsync(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        process.ExitCode.Should().Be(0, $"stdout:\n{stdout}\nstderr:\n{stderr}");

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            (await db.BranchDevices.AsNoTracking().CountAsync(x => x.Status == BranchDevice.ActiveStatus))
                .Should().Be(1);
            (await db.BranchTerminals.AsNoTracking().SingleAsync()).Name.Should().Be("Rust Interop Terminal");
        }

        // Simulated restart: same DATA_DIR, resume-paired mode.
        var resumePsi = new ProcessStartInfo
        {
            FileName = interopExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        resumePsi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
        resumePsi.Environment["BINEXUS_DATA_DIR"] = dataDir;
        resumePsi.Environment["BINEXUS_MODE"] = "resume-paired";
        using var resume = Process.Start(resumePsi)!;
        var resumeOut = await resume.StandardOutput.ReadToEndAsync();
        await resume.WaitForExitAsync();
        resume.ExitCode.Should().Be(0, resumeOut);
        resumeOut.Should().Contain("\"event\":\"paired\"");
    }

    [Fact]
    public async Task Rust_product_client_confirm_after_restart_uses_receipt_reissue_path()
    {
        var interopExe = ResolveInteropExecutable();
        await using var context = await StartListeningBranchAsync();
        using var admin = context.CreateAdminClient();

        var sessionResponse = await admin.PostAsJsonAsync("/branch/pairing/sessions", new { });
        sessionResponse.EnsureSuccessStatusCode();
        var session = (await sessionResponse.Content.ReadFromJsonAsync<SessionDto>())!;

        var dataDir = Path.Join(Path.GetTempPath(), "binexus-rust-reissue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var psi = new ProcessStartInfo
        {
            FileName = interopExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["BINEXUS_BRANCH_BASE_URL"] = context.BaseAddress.TrimEnd('/');
        psi.Environment["BINEXUS_PAIRING_CODE"] = $"{session.PairingSessionId:D}:{session.PairingCode}";
        psi.Environment["BINEXUS_DATA_DIR"] = dataDir;
        psi.Environment["BINEXUS_MODE"] = "reissue";
        psi.Environment["BINEXUS_TERMINAL_NAME"] = "Reissue Terminal";
        psi.Environment["BINEXUS_POLL_SECS"] = "90";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exchanged = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;
            switch (root.GetProperty("event").GetString())
            {
                case "exchanged":
                    exchanged.TrySetResult(Guid.Parse(root.GetProperty("pairingRequestId").GetString()!));
                    break;
                case "paired":
                    paired.TrySetResult();
                    break;
                case "error":
                    exchanged.TrySetException(new InvalidOperationException(e.Data));
                    paired.TrySetException(new InvalidOperationException(e.Data));
                    break;
            }
        };

        process.Start().Should().BeTrue();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var requestId = await exchanged.Task.WaitAsync(TimeSpan.FromSeconds(60));
        // Approve but do not send receipt on the wire to the client — client will reissue after status.
        (await admin.PostAsync($"/branch/pairing/requests/{requestId:D}/approve", null)).EnsureSuccessStatusCode();

        await paired.Task.WaitAsync(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        process.ExitCode.Should().Be(0, stdout.ToString());
        stdout.ToString().Should().Contain("\"event\":\"paired\"");
    }

    private static string ResolveInteropExecutable()
    {
        var repoRoot = FindRepoRoot();
        var candidates = new[]
        {
            Path.Join(repoRoot, "apps", "desktop", "src-tauri", "target", "debug", "pairing_interop.exe"),
            Path.Join(repoRoot, "apps", "desktop", "src-tauri", "target", "release", "pairing_interop.exe"),
            Environment.GetEnvironmentVariable("BINEXUS_PAIRING_INTEROP_EXE") ?? string.Empty,
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            throw new FileNotFoundException(
                "pairing_interop.exe not found. Build with: cargo build --bin pairing_interop (from apps/desktop/src-tauri).");
        }

        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "pnpm-workspace.yaml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root from test BaseDirectory.");
    }

    private async Task<ListeningBranchContext> StartListeningBranchAsync()
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

        var factory = new ListeningBranchFactory(fixture.ConnectionString, SigningKey, Pepper);
        // ASP.NET Core 10+: real Kestrel listener (Rust client needs a TCP port, not TestServer).
        factory.UseKestrel(0);
        factory.StartServer();
        var baseAddress = factory.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()
            ?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not publish a listen address.");
        return new ListeningBranchContext(factory, tenantId, branchId, userId, baseAddress);
    }

    private async Task ResetAsync()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE device_pairing_challenges, device_pairing_requests, device_pairing_sessions,
                branch_devices, branch_terminals, branch_instances CASCADE;
            """);
    }

    private sealed class ListeningBranchContext(
        ListeningBranchFactory factory,
        Guid tenantId,
        Guid branchId,
        Guid userId,
        string baseAddress) : IAsyncDisposable
    {
        public string BaseAddress { get; } = baseAddress;

        public HttpClient CreateAdminClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAdminJwt(tenantId, branchId, userId));
            return client;
        }

        public async ValueTask DisposeAsync() => await factory.DisposeAsync();
    }

    private sealed class ListeningBranchFactory(string connectionString, string signingKey, string pepper)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Jwt:SigningKey", signingKey);
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", pepper);
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
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
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record SessionDto(Guid PairingSessionId, string PairingCode);
}
