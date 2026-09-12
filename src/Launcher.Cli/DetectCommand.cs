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
        var installs = Collect(options.GamePath, out var explicitFailed);
        if (explicitFailed)
        {
            // Bewusst kein Bericht: wir haben nirgendwo gesucht, also darf auch
            // nichts behauptet werden. Den Pfad hat der Aufrufer genannt, nicht wir.
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

    /// <summary>Sucht Installationen, oder untersucht genau die eine genannte.</summary>
    public static IReadOnlyList<GameInstall> Collect(string? explicitPath, out bool explicitFailed)
    {
        explicitFailed = false;
        var inspector = new InstallInspector();

        if (explicitPath is null)
        {
            return new InstallLocator().Locate().Select(inspector.Inspect).ToArray();
        }

        var candidate = ResolveExplicit(explicitPath);
        if (candidate is null)
        {
            explicitFailed = true;
            return [];
        }

        return [inspector.Inspect(candidate)];
    }

    /// <summary>Prüft den genannten Ordner. Die Begründung für null steht auf stderr.</summary>
    public static InstallCandidate? ResolveExplicit(string explicitPath)
    {
        string path;

        try
        {
            path = Path.GetFullPath(explicitPath).TrimEnd('\\');
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.Error.WriteLine($"Ungültiger Pfad: {explicitPath}");
            Console.Error.WriteLine(e.Message);
            return null;
        }

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Der Ordner existiert nicht: {path}");
            return null;
        }

        if (!File.Exists(Path.Combine(path, InstallInspector.ExecutableName)))
        {
            Console.Error.WriteLine(
                $"In {path} liegt keine {InstallInspector.ExecutableName} — das ist kein GTA-IV-Verzeichnis.");
            return null;
        }

        // Auch ein von Hand genannter Ordner verraet seine Herkunft — sonst wuessten
        // wir nicht, ob es dort einen Schalter gegen Updates gibt.
        var platform = new InstallLocator().InferPlatform(path);
        var via = platform == GamePlatform.Unknown
            ? "manuell angegeben"
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
            Console.Error.WriteLine($"Bericht konnte nicht geschrieben werden: {e.Message}");
            return false;
        }
    }
}
