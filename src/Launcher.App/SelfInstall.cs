using System.Diagnostics;
using System.IO;

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
    public static bool IsSetUp =>
        string.Equals(CurrentPath, TargetPath, StringComparison.OrdinalIgnoreCase) &&
        File.Exists(DesktopShortcut);

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
                Directory.CreateDirectory(TargetDirectory);

                // Reading itself is allowed, even while running. Writing over an
                // already running copy is not - so a running target is left
                // alone and reported instead.
                File.Copy(source, TargetPath, overwrite: true);

                messages.Add($"Copied to {TargetDirectory}");
                relaunchNeeded = true;
            }

            CreateShortcut(DesktopShortcut, TargetPath);
            messages.Add("Shortcut created on the desktop");

            CreateShortcut(StartMenuShortcut, TargetPath);
            messages.Add("Added to the start menu");
        }
        catch (IOException e)
        {
            return $"Failed: {e.Message}";
        }
        catch (UnauthorizedAccessException e)
        {
            return $"No permission: {e.Message}";
        }

        return string.Join(". ", messages) + ".";
    }

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
