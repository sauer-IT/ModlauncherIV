using System.Security.Cryptography;
using System.Text;

namespace ModlauncherIV.Core.Backup;

/// <summary>
/// Wo der Launcher seinen Zustand ablegt.
///
/// Bewusst unter %LOCALAPPDATA% und nicht im Spielverzeichnis: das überlebt eine
/// Neuinstallation des Spiels, wird von "Dateien überprüfen" nicht angefasst und
/// braucht keine Administratorrechte.
/// </summary>
public static class AppPaths
{
    public const string ProductFolder = "ModlauncherIV";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductFolder);

    /// <summary>Ordner für eine bestimmte Installation, benannt nach ihrem Pfad.</summary>
    public static string ForInstall(string gameRoot) =>
        Path.Combine(Root, "installs", InstallKey(gameRoot));

    public static string SnapshotsFor(string gameRoot) =>
        Path.Combine(ForInstall(gameRoot), "snapshots");

    public static string LedgerFor(string gameRoot) =>
        Path.Combine(ForInstall(gameRoot), "ledger.json");

    /// <summary>Arbeitsverzeichnis für beschaffte Dateien. Geteilt über Installationen.</summary>
    public static string Cache => Path.Combine(Root, "cache");

    /// <summary>
    /// Der mitgelieferte Rezeptkatalog, neben dem Programm.
    ///
    /// Bewusst nicht das aktuelle Arbeitsverzeichnis: ein über eine Verknüpfung
    /// oder aus dem Startmenü gestartetes Programm hat irgendeins, und dann fände
    /// der Launcher seine eigenen Rezepte nicht.
    /// </summary>
    public static string CatalogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "catalog");

    /// <summary>
    /// Kurzer, stabiler Bezeichner für einen Spielpfad. Der Pfad selbst taugt
    /// nicht als Ordnername, und ein reiner Hash wäre beim Draufschauen nutzlos —
    /// deshalb lesbarer Name plus Hash gegen Kollisionen.
    /// </summary>
    public static string InstallKey(string gameRoot)
    {
        var normalised = Path.GetFullPath(gameRoot)
            .TrimEnd(Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..12].ToLowerInvariant();

        var label = new string(Path.GetFileName(normalised)
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());

        return string.IsNullOrEmpty(label) ? hash : $"{label}-{hash}";
    }
}
