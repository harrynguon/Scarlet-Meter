using System.IO;

namespace ScarletMeter.App;

/// <summary>
/// Command-line switches. <c>--no-capture</c> runs the window without Npcap (UI work, machines without the
/// driver); <c>--data-dir &lt;path&gt;</c> overrides the data directory and must be absolute.
/// </summary>
public sealed record LaunchOptions(bool NoCapture, string? DataDirectory)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var noCapture = false;
        string? dataDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--no-capture":
                    noCapture = true;
                    break;
                case "--data-dir":
                    if (i + 1 >= args.Count) throw new ArgumentException("--data-dir needs a path.");
                    dataDirectory = args[++i];
                    if (!Path.IsPathFullyQualified(dataDirectory))
                        throw new ArgumentException($"--data-dir must be an absolute path, not \"{dataDirectory}\".");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument \"{args[i]}\". Known: --no-capture, --data-dir <absolute path>.");
            }
        }

        return new LaunchOptions(noCapture, dataDirectory);
    }
}
