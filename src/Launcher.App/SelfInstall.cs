using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ModlauncherIV.App;

/// <summary>
/// Sets the program up on the machine.
///
/// A downloaded EXE sits in the downloads folder. Nobody finds it again there,
/// and anyone tidying up their downloads deletes it by accident. So the program
/// can copy itself to a fixed place and create shortcuts — desktop and start
/// menu.
///
/// Under %LOCALAPPDATA%\Programs and not under Program Files: there the user
/// may write without admin rights, the program survives a Windows repair, and a
/// later update needs no prompt.
/// </summary>
/// <summary>What, if anything, is still to be done about the installed copy.</summary>
public enum SetupState
{
    /// <summary>Nothing sits in the fixed place yet, or the shortcut is missing.</summary>
    NotSetUp,

    /// <summary>Something sits there, but it is not the version that is running.</summary>
    Outdated,

    /// <summary>In its place, on the desktop, up to date.</summary>
    Done,
}

public static class SelfInstall
{
    public const string ProgramName = "Modlauncher IV";

    private const string ExecutableName = "ModlauncherIV.exe";

    public static string TargetDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "ModlauncherIV");

    public static string TargetPath => Path.Combine(TargetDirectory, ExecutableName);

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ProgramName + ".lnk");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ProgramName + ".lnk");

    /// <summary>The path of the currently running EXE.</summary>
    public static string CurrentPath => Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// True when the program already sits in its fixed place and can be found on
    /// the desktop. Only then is there nothing left to offer.
    /// </summary>
    public static bool IsSetUp => State == SetupState.Done;

    /// <summary>
    /// What the installed copy looks like from here.
    ///
    /// The outdated case is the one that bites in practice: whoever installed an
    /// older build once starts it from the desktop from then on, and every newer
    /// build sits unnoticed next to it. The program has to say that itself -
    /// nobody compares file dates of their own accord, and from the inside a
    /// stale copy looks exactly like a working one.
    /// </summary>
    public static SetupState State
    {
        get
        {
            var here = CurrentPath;
            if (string.IsNullOrEmpty(here) || !File.Exists(here))
            {
                // Without a path of our own there is nothing to offer, and an
                // offer that cannot be carried out is worse than none.
                return SetupState.Done;
            }

            if (!File.Exists(TargetPath))
            {
                return SetupState.NotSetUp;
            }

            if (!string.Equals(here, TargetPath, StringComparison.OrdinalIgnoreCase)
                && !IsSameFile(here, TargetPath))
            {
                return SetupState.Outdated;
            }

            return File.Exists(DesktopShortcut) ? SetupState.Done : SetupState.NotSetUp;
        }
    }

    /// <summary>
    /// Same size and same timestamp. File.Copy takes the write time along, so the
    /// installed copy of a build carries the date of that build - which makes
    /// this comparison enough, and cheaper than hashing 60 MB on every start.
    /// </summary>
    private static bool IsSameFile(string a, string b)
    {
        try
        {
            var x = new FileInfo(a);
            var y = new FileInfo(b);
            return x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
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

    /// <summary>
    /// Copies itself to the fixed place and creates the shortcuts. Returns what
    /// happened — or why it did not.
    /// </summary>
    public static string Run(out bool relaunchNeeded)
    {
        relaunchNeeded = false;

        var source = CurrentPath;
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            return "The program could not determine its own path.";
        }

        var messages = new List<string>();

        try
        {
            var alreadyThere = string.Equals(source, TargetPath, StringComparison.OrdinalIgnoreCase);

            if (!alreadyThere)
            {
                var replacing = File.Exists(TargetPath);

                Directory.CreateDirectory(TargetDirectory);

                // Reading itself is allowed, even while running. Writing over an
                // already running copy is not - so a running target is left
                // alone and reported instead.
                File.Copy(source, TargetPath, overwrite: true);

                messages.Add(replacing
                    ? $"Replaced the older copy in {TargetDirectory}"
                    : $"Copied to {TargetDirectory}");

                relaunchNeeded = true;
            }

            CreateShortcut(DesktopShortcut, TargetPath);
            messages.Add("Shortcut created on the desktop");

            CreateShortcut(StartMenuShortcut, TargetPath);
            messages.Add("Added to the start menu");

            RegisterUninstall();
        }
        catch (IOException e)
        {
            // The most likely reason by far: the installed copy is open. Windows
            // says "the process cannot access the file", which sends people
            // looking for permissions rather than for a second window.
            return File.Exists(TargetPath)
                ? $"Failed: {e.Message} The installed copy may still be open - close it and try again."
                : $"Failed: {e.Message}";
        }
        catch (UnauthorizedAccessException e)
        {
            return $"No permission: {e.Message}";
        }

        return string.Join(". ", messages) + ".";
    }

    /// <summary>Where Windows keeps its own list of installed programs.</summary>
    public const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModlauncherIV";

    /// <summary>
    /// Puts the program into "Apps &amp; features".
    ///
    /// Under HKCU, matching where it installs: an entry in HKLM would claim it
    /// is there for every account on the machine, which it is not.
    ///
    /// The point is not tidiness. Anybody who tries this and wants it gone again
    /// looks where they look for every other program, and finding nothing there
    /// means deleting a folder by hand and leaving the rest - the shortcuts, the
    /// ledger, the snapshots, and a game that is still modded.
    /// </summary>
    public static void RegisterUninstall()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            if (key is null)
            {
                return;
            }

            key.SetValue("DisplayName", ProgramName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", ProgramName);
            key.SetValue("DisplayIcon", TargetPath);
            key.SetValue("InstallLocation", TargetDirectory);
            key.SetValue("UninstallString", $"\"{TargetPath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            // Rounded, in kilobytes, the way that list wants it.
            if (File.Exists(TargetPath))
            {
                key.SetValue(
                    "EstimatedSize",
                    (int)(new FileInfo(TargetPath).Length / 1024),
                    RegistryValueKind.DWord);
            }
        }
        catch (Exception e) when (e is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            // Not being in that list is a nuisance, not a failure of the
            // installation. Everything else has already been done by here.
        }
    }

    public static void UnregisterUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception e) when (e is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
        }
    }

    private static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Creates a .lnk.
    ///
    /// Through the Windows Script Host via COM, because .NET cannot write
    /// shortcuts itself and the format is binary and undocumented enough not to
    /// rebuild by hand. No extra package: WScript.Shell is on every Windows.
    /// </summary>
    private static void CreateShortcut(string linkPath, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null)
        {
            throw new IOException("WScript.Shell is not available on this system.");
        }

        object? shell = null;
        object? link = null;

        try
        {
            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                throw new IOException("WScript.Shell could not be started.");
            }

            link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                                     null, shell, [linkPath]);

            if (link is null)
            {
                throw new IOException("The shortcut could not be created.");
            }

            var linkType = link.GetType();

            void Set(string name, string value) =>
                linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty,
                                      null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", Path.GetDirectoryName(target) ?? string.Empty);
            Set("Description", "Downgrader and mod installer for GTA IV");

            linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            if (link is not null) { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
            if (shell is not null) { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
    }

    /// <summary>Starts the installed copy and ends this one.</summary>
    public static void RelaunchFromTarget()
    {
        if (!File.Exists(TargetPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = TargetPath,
            UseShellExecute = true,
        });
    }
}
