using System.Text.Json;
using FluentAssertions;

namespace Binexus.ArchitectureTests.Branching;

public sealed class BranchHealthOpenApiTests
{
    [Fact]
    public void OpenApi_artifact_does_not_include_health_branch()
    {
        var path = FindOpenApiArtifact();
        File.Exists(path).Should().BeTrue(because: path);
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("paths").TryGetProperty("/health/branch", out _)
            .Should().BeFalse();
    }

    private static string FindOpenApiArtifact()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, "artifacts", "openapi", "binexus-v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("artifacts/openapi/binexus-v1.json not found.");
    }
}
