using FluentAssertions;

namespace Binexus.ArchitectureTests.Runtime;

public sealed class DockerImageRuntimeModeTests
{
    [Fact]
    public void Final_stage_dockerfiles_do_not_set_RuntimeMode_ENV()
    {
        var repoRoot = FindRepoRoot();
        var api = File.ReadAllText(Path.Join(repoRoot, "infrastructure", "docker", "Dockerfile.api"));
        var workers = File.ReadAllText(Path.Join(repoRoot, "infrastructure", "docker", "Dockerfile.workers"));

        AssertFinalStageHasNoRuntimeMode(api, "Dockerfile.api");
        AssertFinalStageHasNoRuntimeMode(workers, "Dockerfile.workers");
    }

    private static void AssertFinalStageHasNoRuntimeMode(string dockerfile, string name)
    {
        var finalIndex = dockerfile.LastIndexOf("AS final", StringComparison.Ordinal);
        finalIndex.Should().BeGreaterThan(0, because: $"{name} should have a final stage");
        var finalStage = dockerfile[finalIndex..];
        finalStage.Should().NotContain("Binexus__RuntimeMode", because: $"{name} final image must stay runtime-neutral");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
