using System.Reflection;

namespace ScarletMeter.App.Updates;

/// <summary>
/// What a build of the meter was made from, as Updates/BuildStamp.targets stamped it into the assembly: the
/// upstream release the submodule was pinned to (a tag such as v1.11.4.334, or the upstream short commit when
/// the build found no tag) and Scarlet Meter's own short commit. A value the build could not determine, because
/// git or the repository was missing, reads as <see cref="UnknownValue"/>; a blank one does too.
/// </summary>
public sealed record BuildInfo(string UpstreamRelease, string ScarletCommit)
{
    /// <summary>The value of a stamp the build could not determine.</summary>
    public const string UnknownValue = "unknown";

    /// <summary>Assembly metadata key of <see cref="UpstreamRelease"/>.</summary>
    public const string UpstreamReleaseKey = "ScarletUpstreamRelease";

    /// <summary>Assembly metadata key of <see cref="ScarletCommit"/>.</summary>
    public const string ScarletCommitKey = "ScarletCommit";

    /// <summary>The running meter's stamp, read once from the ScarletMeter assembly.</summary>
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    /// <summary>The pinned upstream release tag, or its short commit, or <see cref="UnknownValue"/>.</summary>
    public string UpstreamRelease { get; } = OrUnknown(UpstreamRelease);

    /// <summary>Scarlet Meter's short commit, or <see cref="UnknownValue"/>.</summary>
    public string ScarletCommit { get; } = OrUnknown(ScarletCommit);

    /// <summary>
    /// The pinned upstream release as a comparable version, or null when the build stamped a commit or
    /// <see cref="UnknownValue"/> instead of a release tag.
    /// </summary>
    public Version? UpstreamVersion => UpstreamReleaseTag.TryParse(UpstreamRelease, out var version) ? version : null;

    /// <summary>Reads the stamp from an assembly's metadata; an assembly without one reads as unknown.</summary>
    public static BuildInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        return new BuildInfo(Read(UpstreamReleaseKey), Read(ScarletCommitKey));

        string Read(string key) => metadata.FirstOrDefault(entry => entry.Key == key)?.Value ?? UnknownValue;
    }

    private static string OrUnknown(string? value) => string.IsNullOrWhiteSpace(value) ? UnknownValue : value.Trim();
}
