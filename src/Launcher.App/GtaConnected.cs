using System.IO;
using Microsoft.Win32;

namespace ModlauncherIV.App;

/// <summary>What was found of GTA Connected on this machine.</summary>
/// <param name="LauncherPath">Its Launcher.exe.</param>
/// <param name="GamePath">
/// The GTAIV.exe it is set to start, as it recorded it - not as we detected it.
/// </param>
public sealed record ConnectedInstall(string LauncherPath, string? GamePath);

/// <summary>
/// GTA Connected, the multiplayer client, if it is installed.
///
/// It is not a mod and not a recipe: it installs itself elsewhere, brings its
/// own client and its own xlive.dll, and starts the game itself. Nothing here
/// changes it - the launcher only finds it and offers to start it, because the
/// two are aimed at the same installation and switching between them by hand
/// means going through two start menus.
/// </summary>
public static class GtaConnected
{
    private const string Root = @"SOFTWARE\Jack's Mini Network\Grand Theft Auto Connected";

    /// <summary>
    /// Finds it, or returns null.
    ///
    /// Through the registry rather than by guessing at paths: the key is what
    /// its own launcher reads, so whatever is in there is what will actually
    /// start - including for anyone who installed it somewhere unusual.
    /// </summary>
    public static ConnectedInstall? Find()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root);
            if (key?.GetValue("Installation Directory") is not string directory)
            {
                return null;
            }

            var launcher = Path.Combine(directory, "Launcher.exe");
            if (!File.Exists(launcher))
            {
                // The key outlives an uninstall. A button that starts nothing is
                // worse than no button.
                return null;
            }

            using var game = Registry.CurrentUser.OpenSubKey($@"{Root}\Grand Theft Auto IV");
            var exe = game?.GetValue("Game EXE Path") as string;

            return new ConnectedInstall(launcher, exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether it is set to the installation the launcher is looking after.
    ///
    /// If somebody has two copies of the game, GTA Connected may well be aimed
    /// at the other one - and then none of what the launcher installed applies
    /// to what actually starts. Worth saying, and cheap to check.
    /// </summary>
    public static bool PointsAt(this ConnectedInstall connected, string gameRoot)
    {
        if (connected.GamePath is null)
        {
            return true;
        }

        try
        {
            var theirs = Path.GetFullPath(Path.GetDirectoryName(connected.GamePath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar);

            var ours = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar);

            return string.Equals(theirs, ours, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
