using System.IO;
using System.Reflection;
using System.Text.Json;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Upstream;

namespace ScarletMeter.Tests;

/// <summary>
/// The upstream boundary (AGENTS.md): only the engine may reference upstream code, and nothing of upstream
/// shows through the engine's public surface. These fail when a shortcut is taken, before it spreads.
/// </summary>
public sealed class UpstreamBoundaryTests
{
    private const string EngineLibrary = "ScarletMeter.Engine";

    [Fact]
    public void AppReferencesNoUpstreamAssembly()
    {
        var upstream = typeof(global::ScarletMeter.App.App).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(IsUpstreamAssemblyName);
        Assert.Empty(upstream);
    }

    [Fact]
    public void OnlyTheEngineDeclaresAnUpstreamProjectReference()
    {
        // GetReferencedAssemblies lists only references the compiler used; the build's dependency graph also
        // records an unused ProjectReference, which would still couple a project's build to upstream. The graph
        // holds every Scarlet project this test project references, directly or not, so a new project is
        // guarded once the tests reference it.
        var scarlet = ScarletProjectDependencies();
        Assert.Contains("AionDpsMeter.Services", scarlet[EngineLibrary]); // the graph is read correctly
        var leaks = scarlet
            .Where(project => project.Key != EngineLibrary)
            .SelectMany(project => project.Value.Where(IsUpstreamAssemblyName).Select(dependency => $"{project.Key} -> {dependency}"))
            .ToList();
        Assert.Empty(leaks);
    }

    [Fact]
    public void EnginePublicSurfaceExposesNoUpstreamType()
    {
        var leaks = typeof(ICombatEngine).Assembly.GetExportedTypes().SelectMany(UpstreamTypesShownBy).ToList();
        Assert.Empty(leaks);
    }

    [Fact]
    public void SurfaceCheckSeesUpstreamInterfaces()
    {
        // Guards the guard: this internal type shows upstream only through the interface it implements.
        Assert.Contains(UpstreamTypesShownBy(typeof(NoCaptureDevice)), leak => leak.Contains("IPacketCaptureDevice", StringComparison.Ordinal));
    }

    // Direct dependencies of each Scarlet project library in this test run's dependency graph.
    private static Dictionary<string, List<string>> ScarletProjectDependencies()
    {
        using var deps = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "ScarletMeter.Tests.deps.json")));
        var projects = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "project")
            .Select(library => library.Name)
            .ToHashSet(StringComparer.Ordinal);
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        return target.EnumerateObject()
            .Where(library => projects.Contains(library.Name))
            .Select(library => (Name: library.Name.Split('/')[0], library.Value))
            .Where(library => library.Name.StartsWith("ScarletMeter", StringComparison.Ordinal))
            .ToDictionary(
                library => library.Name,
                library => library.Value.TryGetProperty("dependencies", out var dependencies)
                    ? dependencies.EnumerateObject().Select(dependency => dependency.Name).ToList()
                    : []);
    }

    // Every upstream type a type's shape shows: its base type, interfaces, generic constraints and the
    // signatures of its own public and protected members (a derived type outside the engine sees both).
    private static IEnumerable<string> UpstreamTypesShownBy(Type type)
    {
        foreach (var inherited in InheritedTypesOf(type).Where(IsUpstreamType))
            yield return $"{type.Name}: base, interface or constraint {inherited}";
        var members = type.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var member in members.Where(IsVisibleOutsideAssembly))
        {
            foreach (var memberType in TypesOf(member).Where(IsUpstreamType))
                yield return $"{type.Name}.{member.Name}: {memberType}";
        }
    }

    private static bool IsVisibleOutsideAssembly(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisibleOutsideAssembly),
        EventInfo @event => @event.AddMethod is { } add && IsVisibleOutsideAssembly(add),
        _ => false,
    };

    private static IEnumerable<Type> InheritedTypesOf(Type type)
    {
        if (type.BaseType is { } baseType) yield return baseType;
        foreach (var @interface in type.GetInterfaces()) yield return @interface;
        foreach (var constraint in ConstraintsOf(type.GetGenericArguments())) yield return constraint;
    }

    // GetGenericParameterConstraints throws for a closed argument, so only generic parameters are asked.
    private static IEnumerable<Type> ConstraintsOf(IEnumerable<Type> arguments) =>
        arguments.Where(argument => argument.IsGenericParameter).SelectMany(argument => argument.GetGenericParameterConstraints());

    private static bool IsUpstreamAssemblyName(string? name) =>
        name is not null && name.StartsWith("AionDpsMeter.", StringComparison.Ordinal);

    private static bool IsUpstreamType(Type type)
    {
        if (type.HasElementType) return IsUpstreamType(type.GetElementType()!);
        return IsUpstreamAssemblyName(type.Assembly.GetName().Name) || type.GetGenericArguments().Any(IsUpstreamType);
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)
            .Concat(ConstraintsOf(method.GetGenericArguments())),
        ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => new[] { property.PropertyType },
        FieldInfo field => new[] { field.FieldType },
        EventInfo @event => @event.EventHandlerType is { } handler ? new[] { handler } : Array.Empty<Type>(),
        _ => Array.Empty<Type>(),
    };
}
