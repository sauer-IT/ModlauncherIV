using System.Diagnostics;
using System.Security.Cryptography;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Untersucht einen Kandidaten und liefert das vollständige Bild: Version, Hash,
/// Episoden, Fremddateien und die Schlussfolgerungen daraus.
///
/// Rein lesend. Diese Klasse fasst nichts an.
/// </summary>
public sealed class InstallInspector
{
    public const string ExecutableName = "GTAIV.exe";

    /// <summary>Proxy-DLLs, über die ASI-Loader eingehängt werden.</summary>
    private static readonly string[] AsiLoaderNames =
    [
        "dsound.dll", "dinput8.dll", "d3d9.dll", "xinput1_3.dll", "version.dll", "winmm.dll",
    ];

    /// <summary>Ordner, in denen Mods typischerweise ihre Dateien ablegen.</summary>
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
                "Versionsinformation der Spieldatei nicht lesbar.",
                e.Message));
        }

        var resolved = KnownVersions.Resolve(raw);

        // Die Registry hat bei RGL ebenfalls eine Version. Weichen beide ab,
        // ist die EXE womöglich schon ausgetauscht — das muss sichtbar sein.
        if (candidate.Platform == GamePlatform.RockstarLauncher)
        {
            var registryVersion = ReadRockstarRegistryVersion();
            if (registryVersion is not null &&
                !string.Equals(registryVersion, resolved.Raw, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new Note(
                    NoteLevel.Warning,
                    $"Registry meldet {registryVersion}, die Spieldatei meldet {resolved.Raw}.",
                    "Die EXE wurde vermutlich bereits ausgetauscht, ohne dass der Launcher davon weiß."));
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
                "Prüfsumme der Spieldatei konnte nicht gebildet werden.",
                e.Message));
            return (null, 0);
        }
    }

    // -------------------------------------------------------------- Mod-Spuren

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
                "Das Spielverzeichnis konnte nicht vollständig gelesen werden.",
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

    // --------------------------------------------------------------- Deutung

    /// <summary>
    /// Übersetzt die Rohbefunde in Aussagen, die für den nächsten Schritt zählen.
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
                $"Version {version.Raw} ist dem Launcher nicht bekannt.",
                "Solange die Version nicht im Versionsgraphen steht, darf kein Rezept automatisch laufen."));
        }
        else if (version.IsCompleteEdition)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Complete Edition erkannt — für die meisten Mods ist ein Downgrade nötig.",
                "Radiosongs und Multiplayer wurden von Rockstar entfernt und kommen durch den Downgrade nicht zurück."));
        }
        else if (version.IsModdingTarget)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                $"Version {version.Raw} ist bereits ein Modding-Ziel — kein Downgrade nötig.",
                "Diese Version braucht einen GFWL-Stub (xliveless), um ohne Games for Windows Live zu starten."));
        }

        if (artifacts.Count == 0)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Keine Fremddateien gefunden — die Installation ist unverändert."));
        }
        else
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{artifacts.Count} Fremddatei(en) gefunden — die Installation ist bereits modifiziert.",
                "Der Launcher kennt diese Dateien nicht und kann sie nicht zurücknehmen."));
        }

        if (hasTlad || hasTbogt)
        {
            var episodes = (hasTlad, hasTbogt) switch
            {
                (true, true) => "TLAD und TBoGT",
                (true, false) => "TLAD",
                _ => "TBoGT",
            };

            notes.Add(new Note(
                NoteLevel.Info,
                $"Episodes ({episodes}) liegen im selben Verzeichnis.",
                "Ein Downgrade betrifft sie mit — sie sind kein separates Spiel."));
        }

        switch (candidate.Platform)
        {
            case GamePlatform.RockstarLauncher:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Rockstar Games Launcher: kein dokumentierter Schalter gegen Auto-Update.",
                    "Der Launcher kann die Installation nach einem Downgrade eigenständig zurücksetzen."));
                break;

            case GamePlatform.Steam:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Steam: Auto-Update lässt sich über die appmanifest-Datei sperren.",
                    "\"Dateien überprüfen\" hebt jeden Downgrade trotzdem auf."));
                break;

            case GamePlatform.Epic:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Epic: Auto-Update lässt sich in den Einstellungen deaktivieren.",
                    "\"Verify\" hebt jeden Downgrade trotzdem auf."));
                break;

            case GamePlatform.Unknown:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Herkunft der Installation nicht bestimmbar.",
                    "Ohne bekannte Plattform kann das Auto-Update nicht gesperrt werden."));
                break;

            case GamePlatform.Retail:
            default:
                break;
        }
    }
}
