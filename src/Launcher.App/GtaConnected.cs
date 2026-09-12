using System.IO;
using Microsoft.Win32;

namespace ModlauncherIV.App;

/// <summary>What was found of GTA Connected on this machine.</summary>
/// <param name="LauncherPath">Its Launcher.exe.</param>
/// <param name="GamePath">
/// The GTAIV.exe it is set to start, as it recorded it - not as we detected it.
/// </param>
/// <param name="Version">What its uninstall entry says, or empty.</param>
public sealed record ConnectedInstall(string LauncherPath, string? GamePath, string Version);

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

            return new ConnectedInstall(launcher, exe, ReadVersion());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The servers it last connected to, newest first.
    ///
    /// Read from its own History.xml rather than kept here: it is that file the
    /// list in its launcher is built from, so whatever is shown matches what the
    /// player would see there - and a server that was removed over there
    /// disappears here too, without a second list to keep in step.
    ///
    /// Parsed by hand rather than with an XML reader. The file holds one kind of
    /// element, it is written by the same program every time, and a malformed
    /// one should cost an empty list rather than an exception on the home page.
    /// </summary>
    public static IReadOnlyList<string> RecentServers(this ConnectedInstall connected, int limit = 5)
    {
        try
        {
            var file = Path.Combine(
                Path.GetDirectoryName(connected.LauncherPath) ?? string.Empty, "History.xml");

            if (!File.Exists(file))
            {
                return [];
            }

            var servers = new List<string>();

            foreach (var line in File.ReadAllLines(file))
            {
                var open = line.IndexOf("<Server>", StringComparison.OrdinalIgnoreCase);
                var close = line.IndexOf("</Server>", StringComparison.OrdinalIgnoreCase);

                if (open < 0 || close <= open)
                {
                    continue;
                }

                open += "<Server>".Length;
                var address = line[open..close].Trim();

                // Only what looks like an address. The field is free text in the
                // file, and this ends up on a command line.
                if (address.Length is > 0 and < 64 &&
                    address.All(c => char.IsLetterOrDigit(c) || c is '.' or ':' or '-' or '_'))
                {
                    servers.Add(address);
                }
            }

            servers.Reverse();
            return servers.Take(limit).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The arguments that connect straight to a server.
    ///
    /// Both switches are its own, read out of its launcher's help text rather
    /// than guessed: /connect takes a server, /silent leaves its window out of
    /// the way when there is nothing left to pick.
    /// </summary>
    public static string ConnectArguments(string server) => $"/connect {server} /silent";

    /// <summary>
    /// The version, from the same place Windows takes it for its own list of
    /// installed programs. Empty when the entry is not there - a missing version
    /// is worth less than a wrong one.
    /// </summary>
    private static string ReadVersion()
    {
        const string uninstall =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(uninstall);
            if (key is null)
            {
                continue;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                using var entry = key.OpenSubKey(name);

                if (entry?.GetValue("DisplayName") is string display &&
                    display.Contains("Grand Theft Auto Connected", StringComparison.OrdinalIgnoreCase) &&
                    entry.GetValue("DisplayVersion") is string version)
                {
                    return version;
                }
            }
        }

        return string.Empty;
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
