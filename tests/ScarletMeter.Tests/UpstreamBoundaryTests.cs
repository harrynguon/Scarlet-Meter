using System.IO;
using System.Reflection;
using System.Text.Json;
using ScarletMeter.App;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The upstream boundary (AGENTS.md): only the engine may reference upstream code, and nothing of upstream
/// shows through the engine's public surface. These fail when a shortcut is taken, before it spreads.
/// </summary>
public sealed class UpstreamBoundaryTests
{
    [Fact]
    public void AppReferencesNoUpstreamAssembly()
    {
        var upstream = typeof(global::ScarletMeter.App.App).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(IsUpstreamAssemblyName);
        Assert.Empty(upstream);
    }

    [Fact]
    public void AppDeclaresNoUpstreamProjectReference()
    {
        // GetReferencedAssemblies lists only references the compiler used; the build's dependency graph also
        // records an unused ProjectReference, which would still couple the app's build to upstream.
        using var deps = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "ScarletMeter.Tests.deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var app = target.EnumerateObject().Single(library => library.Name.StartsWith("ScarletMeter/", StringComparison.Ordinal));
        var upstream = app.Value.TryGetProperty("dependencies", out var dependencies)
            ? dependencies.EnumerateObject().Select(dependency => dependency.Name).Where(IsUpstreamAssemblyName).ToList()
            : [];
        Assert.Empty(upstream);
    }

    [Fact]
    public void EnginePublicSurfaceExposesNoUpstreamType()
    {
        var leaks = new List<string>();
        foreach (var type in typeof(ICombatEngine).Assembly.GetExportedTypes())
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                foreach (var memberType in TypesOf(member).Where(IsUpstreamType))
                    leaks.Add($"{type.Name}.{member.Name}: {memberType}");
            }
        }
        Assert.Empty(leaks);
    }

    private static bool IsUpstreamAssemblyName(string? name) =>
        name is not null && name.StartsWith("AionDpsMeter.", StringComparison.Ordinal);

    private static bool IsUpstreamType(Type type)
    {
        if (type.HasElementType) return IsUpstreamType(type.GetElementType()!);
        return IsUpstreamAssemblyName(type.Assembly.GetName().Name) || type.GetGenericArguments().Any(IsUpstreamType);
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => new[] { property.PropertyType },
        FieldInfo field => new[] { field.FieldType },
        EventInfo @event => @event.EventHandlerType is { } handler ? new[] { handler } : Array.Empty<Type>(),
        _ => Array.Empty<Type>(),
    };
}
