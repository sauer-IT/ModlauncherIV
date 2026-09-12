using System.Diagnostics;
using System.Security.Cryptography;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Inspects a candidate and produces the complete picture: version, hash,
/// episodes, foreign files and the conclusions that follow from them.
///
/// Read-only. This class touches nothing.
/// </summary>
public sealed class InstallInspector
{
    public const string ExecutableName = "GTAIV.exe";

    /// <summary>Proxy DLLs that ASI loaders hook themselves in through.</summary>
    private static readonly string[] AsiLoaderNames =
    [
        "dsound.dll", "dinput8.dll", "d3d9.dll", "xinput1_3.dll", "version.dll", "winmm.dll",
    ];

    /// <summary>Folders mods typically drop their files into.</summary>
    private static readonly string[] ModFolderNames =
    [
        "scripts", "plugins", "asi",
    ];

    public GameInstall Inspect(InstallCandidate candidate)
    {
        var notes = new List<Note>();
        var exePath = Path.Combine(candidate.Path, ExecutableName);

        var version = ProbeVersion(exePath, candidate, notes);
        var (hash, size) = ProbeExecutable(exePath, notes);
        var artifacts = ProbeModArtifacts(candidate.Path, notes);

        var hasTlad = Directory.Exists(Path.Combine(candidate.Path, "TLAD"));
        var hasTbogt = Directory.Exists(Path.Combine(candidate.Path, "TBoGT"));

        AddInterpretation(candidate, version, artifacts, hasTlad, hasTbogt, notes);

        return new GameInstall(
            Path: candidate.Path,
            Platform: candidate.Platform,
            FoundVia: candidate.FoundVia,
            Version: version,
            ExecutablePath: exePath,
            ExecutableSha256: hash,
            ExecutableSizeBytes: size,
            HasTlad: hasTlad,
            HasTbogt: hasTbogt,
            ModArtifacts: artifacts,
            Notes: notes);
    }

    // ---------------------------------------------------------------- Version

    private static GameVersionInfo ProbeVersion(
        string exePath,
        InstallCandidate candidate,
        List<Note> notes)
    {
        string? raw = null;

        try
        {
            raw = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notes.Add(new Note(
                NoteLevel.Blocker,
                "Version information of the game file is not readable.",
                e.Message));
        }

        var resolved = KnownVersions.Resolve(raw);

        // With RGL the registry also holds a version. If the two differ, the EXE
        // may already have been swapped — and that has to be visible.
        if (candidate.Platform == GamePlatform.RockstarLauncher)
        {
            var registryVersion = ReadRockstarRegistryVersion();
            if (registryVersion is not null &&
                !string.Equals(registryVersion, resolved.Raw, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new Note(
                    NoteLevel.Warning,
                    $"The registry reports {registryVersion}, the game file reports {resolved.Raw}.",
                    "The EXE has probably been swapped already without the launcher knowing."));
            }
        }

        return resolved;
    }

    private static string? ReadRockstarRegistryVersion()
    {
        foreach (var view in new[]
                 {
                     Microsoft.Win32.RegistryView.Registry32,
                     Microsoft.Win32.RegistryView.Registry64,
                 })
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Rockstar Games\Grand Theft Auto IV")
                            ?? baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto IV");

            if (key?.GetValue("Version") is string v && !string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------- Hash

    private static (string? Hash, long Size) ProbeExecutable(string exePath, List<Note> notes)
    {
        try
        {
            var info = new FileInfo(exePath);
            using var stream = File.OpenRead(exePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return (hash, info.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                "The checksum of the game file could not be computed.",
                e.Message));
            return (null, 0);
        }
    }

    // ------------------------------------------------------------- Mod traces

    private static IReadOnlyList<ModArtifact> ProbeModArtifacts(string root, List<Note> notes)
    {
        var artifacts = new List<ModArtifact>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var kind = ClassifyFile(name);

                if (kind is not null)
                {
                    artifacts.Add(new ModArtifact(name, kind.Value, SafeLength(file)));
                }
            }

            foreach (var folder in ModFolderNames)
            {
                var path = Path.Combine(root, folder);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                artifacts.Add(new ModArtifact(folder + @"\", ModArtifactKind.ScriptFolder, 0));

                foreach (var file in Directory.EnumerateFiles(path, "*.asi", SearchOption.AllDirectories))
                {
                    artifacts.Add(new ModArtifact(
                        Path.GetRelativePath(root, file),
                        ModArtifactKind.AsiPlugin,
                        SafeLength(file)));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                "The game directory could not be read completely.",
                e.Message));
        }

        return artifacts;
    }

    private static ModArtifactKind? ClassifyFile(string fileName)
    {
        if (fileName.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.StartsWith("ScriptHookDotNet", StringComparison.OrdinalIgnoreCase)
                ? ModArtifactKind.ScriptHookDotNet
                : ModArtifactKind.AsiPlugin;
        }

        if (fileName.Equals("xlive.dll", StringComparison.OrdinalIgnoreCase))
        {
            return ModArtifactKind.Xlive;
        }

        if (fileName.Equals("ScriptHook.dll", StringComparison.OrdinalIgnoreCase))
        {
            return ModArtifactKind.ScriptHook;
        }

        if (AsiLoaderNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return ModArtifactKind.AsiLoader;
        }

        return null;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // ----------------------------------------------------------- Interpretation

    /// <summary>
    /// Turns the raw findings into statements that matter for the next step.
    /// </summary>
    private static void AddInterpretation(
        InstallCandidate candidate,
        GameVersionInfo version,
        IReadOnlyList<ModArtifact> artifacts,
        bool hasTlad,
        bool hasTbogt,
        List<Note> notes)
    {
        if (!version.IsKnown)
        {
            notes.Add(new Note(
                NoteLevel.Blocker,
                $"Version {version.Raw} is not known to the launcher.",
                "As long as the version is not in the version graph, no recipe may run automatically."));
        }
        else if (version.IsCompleteEdition)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Complete Edition detected — most mods need a downgrade first.",
                "Rockstar removed radio songs and multiplayer; a downgrade does not bring them back."));
        }
        else if (version.IsModdingTarget)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                $"Version {version.Raw} is already a modding target — no downgrade needed.",
                "This version needs a GFWL stub (xliveless) to start without Games for Windows Live."));
        }

        if (artifacts.Count == 0)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "No foreign files found — the installation is unchanged."));
        }
        else
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{artifacts.Count} foreign file(s) found — the installation is already modified.",
                "The launcher does not know these files and cannot take them back."));
        }

        if (hasTlad || hasTbogt)
        {
            var episodes = (hasTlad, hasTbogt) switch
            {
                (true, true) => "TLAD and TBoGT",
                (true, false) => "TLAD",
                _ => "TBoGT",
            };

            notes.Add(new Note(
                NoteLevel.Info,
                $"Episodes ({episodes}) live in the same directory.",
                "A downgrade affects them too — they are not a separate game."));
        }

        switch (candidate.Platform)
        {
            case GamePlatform.RockstarLauncher:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Rockstar Games Launcher: no documented switch against auto-update.",
                    "The launcher can reset the installation on its own after a downgrade."));
                break;

            case GamePlatform.Steam:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Steam: auto-update can be locked via the appmanifest file.",
                    "\"Verify files\" still undoes any downgrade."));
                break;

            case GamePlatform.Epic:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Epic: auto-update can be disabled in the settings.",
                    "\"Verify\" still undoes any downgrade."));
                break;

            case GamePlatform.Unknown:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "The origin of this installation cannot be determined.",
                    "Without a known platform the auto-update cannot be locked."));
                break;

            case GamePlatform.Retail:
            default:
                break;
        }
    }
}
