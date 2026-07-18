using System.Text.Json;
using System.Text.Json.Nodes;
using Binexus.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Binexus.IntegrationTests.Branching;

/// <summary>
/// Branch <c>branch-v1</c> OpenAPI contract for the desktop Rust client.
/// Regenerate artifact: <c>$env:BINEXUS_UPDATE_OPENAPI=1; dotnet test ... --filter FullyQualifiedName~DevicePairingOpenApiContractTests</c>
/// </summary>
[Collection("postgres")]
public sealed class DevicePairingOpenApiContractTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private static readonly string[] ExpectedRoutes =
    [
        "/health/runtime",
        "/health/branch",
        "/branch/terminals",
        "/branch/pairing/sessions",
        "/branch/pairing/requests/{pairingRequestId}",
        "/branch/pairing/requests/{pairingRequestId}/approve",
        "/branch/pairing/requests/{pairingRequestId}/reject",
        "/branch/devices",
        "/branch/devices/{deviceId}/revoke",
        "/branch/pairing/challenges",
        "/branch/pairing/exchange",
        "/branch/pairing/requests/{pairingRequestId}/status",
        "/branch/pairing/requests/{pairingRequestId}/receipt/challenges",
        "/branch/pairing/requests/{pairingRequestId}/receipt/reissue",
        "/branch/pairing/confirm",
    ];

    private static readonly (string Path, string Method, string Schema)[] MachineResponseSchemas =
    [
        ("/branch/pairing/challenges", "post", "CreateExchangeChallengeResponse"),
        ("/branch/pairing/exchange", "post", "PairingExchangeResponse"),
        ("/branch/pairing/requests/{pairingRequestId}/status", "post", "PairingStatusResponse"),
        ("/branch/pairing/requests/{pairingRequestId}/receipt/challenges", "post", "CreateReceiptReissueChallengeResponse"),
        ("/branch/pairing/requests/{pairingRequestId}/receipt/reissue", "post", "ReissuePairingReceiptResponse"),
        ("/branch/pairing/confirm", "post", "PairingConfirmResponse"),
    ];

    [Fact]
    public async Task Branch_document_is_complete_reproducible_and_matches_committed_artifact()
    {
        await fixture.ApplyMigrationsAsync();
        await using var factory = CreateBranchFactory();
        using var client = factory.CreateClient();

        var first = await FetchNormalizedDocumentAsync(client);
        var second = await FetchNormalizedDocumentAsync(client);
        first.Should().Be(second, because: "two consecutive generations must be identical");

        using var doc = JsonDocument.Parse(first);
        var paths = doc.RootElement.GetProperty("paths");
        paths.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(ExpectedRoutes);

        foreach (var route in paths.EnumerateObject())
        {
            route.Name.Should().MatchRegex("^/(branch/|health/)");
        }

        AssertHealthSchemas(doc);
        AssertMachineResponseSchemas(doc);
        AssertProblemResponses(doc, "/branch/pairing/exchange", "post");
        AssertNoSecretExamples(first);

        first.Should().NotContain("localhost");
        first.Should().NotContain("development-only-branch-pairing-pepper");
        first.Should().NotMatchRegex(@"""example""\s*:\s*""\d{8}""");

        var artifactPath = FindCommittedArtifactPath();
        if (string.Equals(Environment.GetEnvironmentVariable("BINEXUS_UPDATE_OPENAPI"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            await File.WriteAllTextAsync(artifactPath, first);
            return;
        }

        File.Exists(artifactPath).Should().BeTrue(because: $"run `$env:BINEXUS_UPDATE_OPENAPI=1; dotnet test ... --filter FullyQualifiedName~DevicePairingOpenApiContractTests` to create {artifactPath}");
        var committed = NormalizeJson(await File.ReadAllTextAsync(artifactPath));
        first.Should().Be(committed, because: "regenerate with BINEXUS_UPDATE_OPENAPI=1 when the Branch surface changes");
    }

    private static void AssertHealthSchemas(JsonDocument doc)
    {
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("RuntimeHealthResponse", out _).Should().BeTrue();
        schemas.TryGetProperty("BranchHealthResponse", out _).Should().BeTrue();

        var runtime = doc.RootElement.GetProperty("paths").GetProperty("/health/runtime").GetProperty("get");
        runtime.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/RuntimeHealthResponse");
    }

    private static void AssertMachineResponseSchemas(JsonDocument doc)
    {
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var (path, method, schemaName) in MachineResponseSchemas)
        {
            schemas.TryGetProperty(schemaName, out var schema).Should().BeTrue($"missing schema {schemaName}");
            schema.TryGetProperty("properties", out _).Should().BeTrue(schemaName);

            var operation = doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
            var refSchema = operation.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
            refSchema.Should().Be($"#/components/schemas/{schemaName}");
        }

        schemas.GetProperty("PairingStatusResponse").GetProperty("properties").TryGetProperty("pairingReceipt", out _).Should().BeTrue();
        schemas.GetProperty("PairingExchangeResponse").GetProperty("properties").TryGetProperty("pairingStatusToken", out _).Should().BeTrue();
    }

    private static void AssertProblemResponses(JsonDocument doc, string path, string method)
    {
        var operation = doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
        var responses = operation.GetProperty("responses");
        responses.TryGetProperty("400", out var badRequest).Should().BeTrue();
        badRequest.GetProperty("content").GetProperty("application/problem+json").Should().NotBeNull();
    }

    private static void AssertNoSecretExamples(string json)
    {
        json.Should().NotMatchRegex(@"""example""\s*:\s*""[^""]*pairingReceipt");
        json.Should().NotMatchRegex(@"""example""\s*:\s*""[^""]*pairingStatusToken");
        json.Should().NotMatchRegex(@"""example""\s*:\s*""\d{8}""");
    }

    private static async Task<string> FetchNormalizedDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/openapi/branch-v1.json");
        response.EnsureSuccessStatusCode();
        return NormalizeJson(await response.Content.ReadAsStringAsync());
    }

    private static string NormalizeJson(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        return node.ToJsonString(PrettyJson);
    }

    private static string FindCommittedArtifactPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, "artifacts", "openapi", "binexus-branch-v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("artifacts/openapi/binexus-branch-v1.json not found.");
    }

    private WebApplicationFactory<Program> CreateBranchFactory() =>
        new ContractTestFactory(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("BranchCloud:BaseUrl", "http://cloud.invalid");
            builder.UseSetting("BranchCredentialStore:Provider", "InMemory");
            builder.UseSetting("BranchPairing:CodePepper", "integration-test-branch-pairing-pepper-0000");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

    private sealed class ContractTestFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }
}
