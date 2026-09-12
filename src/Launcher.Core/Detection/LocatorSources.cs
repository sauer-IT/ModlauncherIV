using System.Security;
using Microsoft.Win32;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Where the locator asks Steam and Epic where they put things.
///
/// Normally both come from this machine, and <see cref="System"/> is what every
/// caller uses. They exist as a parameter for two reasons: an installation that
/// belongs to another Windows account leaves nothing in this user's registry,
/// and detection that cannot be pointed somewhere is detection nobody can test
/// without owning every store.
/// </summary>
/// <param name="SteamPath">Steam's own folder — the one holding steamapps.</param>
/// <param name="EpicManifestDirectory">
/// Folder with Epic's .item manifests, one per installed game.
/// </param>
public sealed record LocatorSources(string? SteamPath, string? EpicManifestDirectory)
{
    /// <summary>What this machine says.</summary>
    public static LocatorSources System { get; } = new(ReadSteamPath(), DefaultEpicManifests());

    /// <summary>
    /// The system's answers, with anything given here put in their place. A
    /// caller naming only Steam keeps the machine's Epic.
    /// </summary>
    public static LocatorSources Override(string? steamPath, string? epicManifests) => new(
        string.IsNullOrWhiteSpace(steamPath) ? System.SteamPath : steamPath.TrimEnd('\\'),
        string.IsNullOrWhiteSpace(epicManifests) ? System.EpicManifestDirectory : epicManifests.TrimEnd('\\'));

    private static string? ReadSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;

            // Steam writes its own path with forward slashes.
            return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\').TrimEnd('\\');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    private static string DefaultEpicManifests() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");
}
