using ScarletMeter.WorldEvents;

namespace ScarletMeter.Tests;

/// <summary>
/// The world-events module stays independent (docs/ARCHITECTURE.md, "World events"): it references no other Scarlet
/// project, no upstream assembly, no UI framework and no package, only the .NET base library, so it can move to
/// another meter unchanged. In the style of <see cref="UpstreamBoundaryTests"/>.
/// </summary>
public sealed class WorldEventsBoundaryTests
{
    private const string Library = "ScarletMeter.WorldEvents";

    [Fact]
    public void ModuleReferencesOnlyTheBaseLibrary()
    {
        var references = typeof(EventRule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? "").ToList();
        Assert.Contains("System.Runtime", references); // the list is read correctly
        Assert.DoesNotContain(references, name =>
            name.StartsWith("ScarletMeter.", StringComparison.Ordinal) || name.StartsWith("AionDpsMeter.", StringComparison.Ordinal));
        Assert.All(references, name => Assert.True(
            name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard",
            $"{Library} references {name}, which is not part of the .NET base library."));
    }

    [Fact]
    public void ModuleDeclaresNoDependencyInTheBuild()
    {
        // The build's graph also records references the compiler did not use, such as an unused ProjectReference or package.
        var projects = UpstreamBoundaryTests.ScarletProjectDependencies();
        Assert.Contains("ScarletMeter.Engine", projects.Keys); // the graph is read correctly
        Assert.Empty(Assert.Contains(Library, projects));
    }
}
