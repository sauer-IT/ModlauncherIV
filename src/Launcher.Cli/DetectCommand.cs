using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Diagnostics;

namespace ModlauncherIV.Cli;

internal static class DetectCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(CliOptions options)
    {
        var installs = Collect(options, out var explicitFailed);
        if (explicitFailed)
        {
            // Deliberately no report: we searched nowhere, so nothing may be
            // claimed either. The caller named the path, not us.
            return ExitCode.NothingFound;
        }

        var report = new DiagnosticReport(
            GeneratedAt: DateTimeOffset.Now,
            System: SystemEnvironmentProbe.Probe(),
            Installs: installs);

        var output = options.AsJson
            ? JsonSerializer.Serialize(report, JsonOptions)
            : ReportRenderer.Render(report);

        if (options.OutputFile is not null)
        {
            if (!TryWrite(options.OutputFile, output))
            {
                return ExitCode.OutputFailed;
            }
        }
        else
        {
            Console.WriteLine(output);
        }

        if (installs.Count == 0)
        {
            return ExitCode.NothingFound;
        }

        return report.HasBlocker ? ExitCode.Blocked : ExitCode.Ok;
    }

    /// <summary>Looks for installations, or inspects exactly the one that was named.</summary>
    public static IReadOnlyList<GameInstall> Collect(CliOptions options, out bool explicitFailed)
    {
        explicitFailed = false;
        var inspector = new InstallInspector();
        var sources = LocatorSources.Override(options.SteamPath, options.EpicManifests);

        if (options.GamePath is null)
        {
            return new InstallLocator(sources).Locate().Select(inspector.Inspect).ToArray();
        }

        var candidate = ResolveExplicit(options.GamePath);
        if (candidate is null)
        {
            explicitFailed = true;
            return [];
        }

        return [inspector.Inspect(candidate)];
    }

    /// <summary>Checks the given folder. The reason for null goes to stderr.</summary>
    public static InstallCandidate? ResolveExplicit(string explicitPath)
    {
        string path;

        try
        {
            path = Path.GetFullPath(explicitPath).TrimEnd('\\');
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.Error.WriteLine($"Invalid path: {explicitPath}");
            Console.Error.WriteLine(e.Message);
            return null;
        }

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"The folder does not exist: {path}");
            return null;
        }

        // Somebody pointing at a Complete Edition points at the folder the store
        // shows them, which holds GTAIV\ and EFLC\ rather than an EXE. Say where
        // we went instead of turning them away over one folder.
        var resolved = InstallLocator.ResolveGameFolder(path);
        if (resolved is null)
        {
            Console.Error.WriteLine(
                $"There is no {InstallInspector.ExecutableName} in {path} - that is not a GTA IV directory.");
            return null;
        }

        if (!string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"The game is one folder down: {resolved}");
            path = resolved;
        }

        // A folder named by hand gives away its origin too - otherwise we would not
        // know whether there is a switch against updates there.
        var platform = new InstallLocator().InferPlatform(path);
        var via = platform == GamePlatform.Unknown
            ? "given by hand"
            : $"manuell angegeben, erkannt als {platform}";

        return new InstallCandidate(path, platform, via);
    }

    private static bool TryWrite(string file, string content)
    {
        try
        {
            File.WriteAllText(file, content, new UTF8Encoding(false));
            Console.WriteLine($"Bericht geschrieben: {Path.GetFullPath(file)}");
            return true;
        }
        catch (Exception e) when (e is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or ArgumentException
                                       or PathTooLongException)
        {
            Console.Error.WriteLine($"The report could not be written: {e.Message}");
            return false;
        }
    }
}
