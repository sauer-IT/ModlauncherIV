using System.Text.RegularExpressions;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Protection;

public enum GuardState
{
    /// <summary>Die Plattform kann das Spiel nicht ungefragt aktualisieren.</summary>
    Locked,

    /// <summary>Die Plattform darf aktualisieren. Ein Downgrade ist in Gefahr.</summary>
    Unlocked,

    /// <summary>Für diese Plattform gibt es keinen Schalter, den wir setzen könnten.</summary>
    NoSwitch,

    Unknown,
}

public sealed record GuardStatus(
    GuardState State,
    string Summary,
    string? Detail,
    string? ManifestPath,
    IReadOnlyList<string> Instructions);

/// <summary>
/// Hält die Plattform davon ab, einen Downgrade zurückzudrehen.
///
/// Das ist bewusst kein Rezept: ein Rezept läuft einmal, diese Sperre muss bei
/// jedem Start neu geprüft werden. Steam setzt sein appmanifest bei Gelegenheit
/// selbst zurück, und "Dateien überprüfen" hebt ohnehin jeden Downgrade auf.
///
/// Für den Rockstar Games Launcher gibt es keinen Schalter. Der Weg dort ist ein
/// anderer: mit FusionFix und dessen Legacy Addon startet GTAIV.exe direkt, der
/// Launcher ist aus dem Spiel. Das ist zuverlässiger als der Offline-Modus, der
/// nach Launcher-Updates immer wieder aufhört zu wirken.
/// </summary>
public static class UpdateGuard
{
    private const string AutoUpdateKey = "AutoUpdateBehavior";

    /// <summary>"1" = nur beim Starten aktualisieren. "2" wäre nie, ist aber unzuverlässig.</summary>
    private const string LockedValue = "1";

    public static GuardStatus Check(GameInstall install) => install.Platform switch
    {
        GamePlatform.Steam => CheckSteam(install),
        GamePlatform.RockstarLauncher => new GuardStatus(
            GuardState.NoSwitch,
            "Rockstar Games Launcher: kein Schalter gegen Updates.",
            "Der Launcher lässt sich nicht davon abhalten, die Installation zu prüfen und zurückzusetzen.",
            null,
            [
                "FusionFix und FusionFix Legacy Addon ins Spielverzeichnis entpacken.",
                "Das Spiel danach direkt über GTAIV.exe starten, nicht über den Launcher.",
                "Achtung: das Überspringen des Launchers kann den Zugang zu TLAD und TBoGT kappen.",
            ]),

        GamePlatform.Epic => new GuardStatus(
            GuardState.NoSwitch,
            "Epic: Auto-Update lässt sich nur in den Einstellungen abschalten.",
            "Es gibt keine Datei, die wir dafür setzen könnten.",
            null,
            [
                "In der Epic-Bibliothek beim Spiel die automatischen Updates deaktivieren.",
                "Niemals \"Verify\" ausführen — das stellt den Originalzustand wieder her.",
            ]),

        GamePlatform.Retail => new GuardStatus(
            GuardState.Locked,
            "Retail-Installation: nichts aktualisiert hier von selbst.",
            null,
            null,
            []),

        _ => new GuardStatus(
            GuardState.Unknown,
            "Herkunft der Installation unbekannt.",
            "Ohne bekannte Plattform lässt sich nicht sagen, ob etwas zurückpatchen kann.",
            null,
            []),
    };

    private static GuardStatus CheckSteam(GameInstall install)
    {
        var manifest = InstallLocator.FindSteamManifest(install.Path);

        if (manifest is null)
        {
            return new GuardStatus(
                GuardState.Unknown,
                "Steam-Manifest nicht gefunden.",
                $"Gesucht wurde nach appmanifest_{InstallLocator.SteamAppId}.acf oberhalb des Spielordners.",
                null,
                []);
        }

        var value = ReadValue(manifest, AutoUpdateKey);

        return value == LockedValue
            ? new GuardStatus(
                GuardState.Locked,
                "Steam aktualisiert nur beim Starten.",
                "Achtung: \"Dateien überprüfen\" hebt einen Downgrade trotzdem auf.",
                manifest,
                [])
            : new GuardStatus(
                GuardState.Unlocked,
                $"Steam darf jederzeit aktualisieren ({AutoUpdateKey} = {value ?? "nicht gesetzt"}).",
                "Ein Downgrade kann dadurch ohne Vorwarnung zurückgesetzt werden.",
                manifest,
                ["mliv guard --apply setzt den Schalter."]);
    }

    /// <summary>
    /// Setzt die Sperre. Legt vorher eine Kopie des Manifests an — es liegt
    /// außerhalb des Spielverzeichnisses und damit außerhalb der Snapshots.
    /// </summary>
    public static bool TryLock(GameInstall install, out string message)
    {
        if (install.Platform != GamePlatform.Steam)
        {
            message = "Nur bei Steam gibt es einen Schalter, den wir setzen könnten.";
            return false;
        }

        var manifest = InstallLocator.FindSteamManifest(install.Path);
        if (manifest is null)
        {
            message = $"appmanifest_{InstallLocator.SteamAppId}.acf nicht gefunden.";
            return false;
        }

        try
        {
            var backup = manifest + ".mliv-backup";
            if (!File.Exists(backup))
            {
                File.Copy(manifest, backup);
            }

            var content = File.ReadAllText(manifest);
            var pattern = $"\"{AutoUpdateKey}\"\\s*\"[^\"]*\"";
            var replacement = $"\"{AutoUpdateKey}\"\t\t\"{LockedValue}\"";

            var updated = Regex.IsMatch(content, pattern)
                ? Regex.Replace(content, pattern, replacement)
                : InsertValue(content, replacement);

            if (updated is null)
            {
                message = "Das Manifest hat ein unerwartetes Format und wurde nicht angefasst.";
                return false;
            }

            File.WriteAllText(manifest, updated);
            message = $"{AutoUpdateKey} auf {LockedValue} gesetzt. Sicherung: {Path.GetFileName(backup)}";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"Manifest nicht schreibbar: {e.Message}";
            return false;
        }
    }

    /// <summary>Fügt den Schlüssel nach der öffnenden Klammer ein, wenn er fehlt.</summary>
    private static string? InsertValue(string content, string line)
    {
        var brace = content.IndexOf('{');
        return brace < 0
            ? null
            : content.Insert(brace + 1, Environment.NewLine + "\t" + line);
    }

    private static string? ReadValue(string file, string key)
    {
        try
        {
            var match = Regex.Match(
                File.ReadAllText(file), $"\"{Regex.Escape(key)}\"\\s*\"([^\"]*)\"");

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
