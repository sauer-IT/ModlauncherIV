using System.Security.Cryptography;
using System.Text;

namespace ModlauncherIV.Core.Backup;

/// <summary>
/// Where the launcher keeps its state.
///
/// Under %LOCALAPPDATA% and deliberately not in the game directory: that
/// survives a reinstall of the game, is not touched by "verify files", and
/// needs no administrator rights.
/// </summary>
public static class AppPaths
{
    public const string ProductFolder = "ModlauncherIV";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductFolder);

    /// <summary>Folder for one installation, named after its path.</summary>
    public static string ForInstall(string gameRoot) =>
        Path.Combine(Root, "installs", InstallKey(gameRoot));

    public static string SnapshotsFor(string gameRoot) =>
        Path.Combine(ForInstall(gameRoot), "snapshots");

    public static string LedgerFor(string gameRoot) =>
        Path.Combine(ForInstall(gameRoot), "ledger.json");

    /// <summary>Working directory for acquired files. Shared across installations.</summary>
    public static string Cache => Path.Combine(Root, "cache");

    /// <summary>
    /// The shipped recipe catalog, next to the program.
    ///
    /// Deliberately not the current working directory: a program started from a
    /// shortcut or the start menu has an arbitrary one, and then the launcher
    /// would not find its own recipes.
    /// </summary>
    public static string CatalogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "catalog");

    /// <summary>
    /// Files the launcher brings along itself — first of all its own trainer.
    /// Putting it online just so the launcher can download it again would be a
    /// detour with one more thing that can fail.
    /// </summary>
    public static string BundledDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "bundled");

    /// <summary>
    /// Short, stable identifier for a game path. The path itself is no good as a
    /// folder name, and a bare hash would be useless to look at — hence a
    /// readable name plus a hash against collisions.
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
