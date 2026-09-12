using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Finds GTA IV installations. Deliberately looks in several places and reports
/// every hit together with its source — when two sources disagree, the user
/// wants to see in the report which one claimed what.
/// </summary>
public sealed class InstallLocator
{
    /// <summary>Steam app id of Grand Theft Auto IV.</summary>
    public const int SteamAppId = 12210;

    private static readonly string[] CommonRelativePaths =
    [
        @"Rockstar Games\Grand Theft Auto IV",
        @"Steam\steamapps\common\Grand Theft Auto IV",
        @"Epic Games\GTAIV",
        @"Grand Theft Auto IV",
    ];

    /// <summary>Returns every candidate found, deduplicated by path.</summary>
    public IReadOnlyList<InstallCandidate> Locate()
    {
        var found = new List<InstallCandidate>();

        found.AddRange(FromRockstarRegistry());
        found.AddRange(FromSteam());
        found.AddRange(FromEpic());
        found.AddRange(FromCommonPaths());

        // First hit per path wins — the order above is the order of reliability.
        return found
            .Where(c => LooksLikeGameFolder(c.Path))
            .GroupBy(c => NormalisePath(c.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    // ---------------------------------------------------------------- Rockstar

    private static IEnumerable<InstallCandidate> FromRockstarRegistry()
    {
        string[] subKeys =
        [
            @"SOFTWARE\Rockstar Games\Grand Theft Auto IV",
            @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto IV",
        ];

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

            foreach (var subKey in subKeys)
            {
                using var key = baseKey.OpenSubKey(subKey);
                var path = key?.GetValue("InstallFolder") as string;

                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return new InstallCandidate(
                        path.TrimEnd('\\'),
                        GamePlatform.RockstarLauncher,
                        $@"Registry HKLM\{subKey}");
                }
            }
        }
    }

    // ------------------------------------------------------------------- Steam

    private static IEnumerable<InstallCandidate> FromSteam()
    {
        var steamPath = ReadSteamPath();
        if (steamPath is null)
        {
            yield break;
        }

        foreach (var library in EnumerateSteamLibraries(steamPath))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest))
            {
                continue;
            }

            var installDir = ReadVdfValue(manifest, "installdir");
            if (installDir is null)
            {
                continue;
            }

            yield return new InstallCandidate(
                Path.Combine(library, "steamapps", "common", installDir),
                GamePlatform.Steam,
                $"Steam appmanifest_{SteamAppId}.acf");
        }
    }

    private static string? ReadSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamPath)
    {
        yield return steamPath;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }

        string content;
        try
        {
            content = File.ReadAllText(vdf);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
        {
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    private static string? ReadVdfValue(string file, string key)
    {
        try
        {
            var match = Regex.Match(
                File.ReadAllText(file),
                $"\"{Regex.Escape(key)}\"\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------- Epic

    private static IEnumerable<InstallCandidate> FromEpic()
    {
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(manifestDir, "*.item");
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            string? location = null;
            string? name = null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                if (root.TryGetProperty("DisplayName", out var n))
                {
                    name = n.GetString();
                }

                if (root.TryGetProperty("InstallLocation", out var l))
                {
                    location = l.GetString();
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                continue;
            }

            if (location is null || name is null)
            {
                continue;
            }

            if (name.Contains("Grand Theft Auto IV", StringComparison.OrdinalIgnoreCase))
            {
                yield return new InstallCandidate(
                    location.TrimEnd('\\'),
                    GamePlatform.Epic,
                    $"Epic-Manifest {Path.GetFileName(file)}");
            }
        }
    }

    // ------------------------------------------------------- Pfad-Heuristiken

    private static IEnumerable<InstallCandidate> FromCommonPaths()
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            roots.Add(Path.Combine(drive.Name, "Games"));
            roots.Add(drive.Name);
        }

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            foreach (var relative in CommonRelativePaths)
            {
                var candidate = Path.Combine(root, relative);

                bool exists;
                try
                {
                    exists = Directory.Exists(candidate);
                }
                catch (IOException)
                {
                    continue;
                }

                if (exists)
                {
                    yield return new InstallCandidate(
                        candidate,
                        GamePlatform.Unknown,
                        "Pfad-Heuristik");
                }
            }
        }
    }

    // ----------------------------------------------------------------- Helpers

    /// <summary>
    /// Determines the origin of a manually specified folder.
    ///
    /// If somebody points --path at their Steam installation, it should still be
    /// recognised as Steam — otherwise the launcher would not know there is a
    /// switch against updates there.
    /// </summary>
    public GamePlatform InferPlatform(string path)
    {
        var normalised = NormalisePath(path);

        var known = Locate().FirstOrDefault(c =>
            string.Equals(NormalisePath(c.Path), normalised, StringComparison.OrdinalIgnoreCase));

        if (known is not null && known.Platform != GamePlatform.Unknown)
        {
            return known.Platform;
        }

        // Even an installation we did not find gives itself away through the Steam
        // manifest sitting above the game folder.
        return FindSteamManifest(path) is not null ? GamePlatform.Steam : GamePlatform.Unknown;
    }

    /// <summary>
    /// Das Manifest liegt in steamapps, das Spiel in steamapps/common/&lt;Name&gt;.
    /// So we walk upwards instead of asking Steam again.
    /// </summary>
    public static string? FindSteamManifest(string gamePath)
    {
        try
        {
            var directory = new DirectoryInfo(gamePath);

            for (var i = 0; i < 4 && directory is not null; i++)
            {
                var candidate = Path.Combine(directory.FullName, $"appmanifest_{SteamAppId}.acf");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        return null;
    }

    /// <summary>A folder only counts as a hit when the main EXE is inside it.</summary>
    private static bool LooksLikeGameFolder(string path)
    {
        try
        {
            return File.Exists(Path.Combine(path, InstallInspector.ExecutableName));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalisePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
