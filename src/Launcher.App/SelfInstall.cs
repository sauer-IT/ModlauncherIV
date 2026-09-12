using System.Diagnostics;
using System.IO;

namespace ModlauncherIV.App;

/// <summary>
/// Richtet das Programm auf dem Rechner ein.
///
/// Eine heruntergeladene EXE liegt im Downloads-Ordner. Dort findet sie
/// niemand wieder, und wer Downloads aufräumt, löscht sie versehentlich. Also
/// kann sich das Programm an einen festen Platz kopieren und Verknüpfungen
/// anlegen — Desktop und Startmenü.
///
/// Unter %LOCALAPPDATA%\Programs und nicht unter Program Files: dort darf der
/// Nutzer ohne Adminrechte schreiben, das Programm überlebt eine
/// Windows-Reparatur, und ein späteres Aktualisieren braucht keine Rückfrage.
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

    /// <summary>Der Pfad der gerade laufenden EXE.</summary>
    public static string CurrentPath => Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// True, wenn das Programm schon an seinem festen Platz liegt und auf dem
    /// Desktop auffindbar ist. Nur dann gibt es nichts mehr anzubieten.
    /// </summary>
    public static bool IsSetUp =>
        string.Equals(CurrentPath, TargetPath, StringComparison.OrdinalIgnoreCase) &&
        File.Exists(DesktopShortcut);

    /// <summary>
    /// Kopiert sich an den festen Platz und legt die Verknüpfungen an.
    /// Gibt zurück, was passiert ist — oder warum nicht.
    /// </summary>
    public static string Run(out bool relaunchNeeded)
    {
        relaunchNeeded = false;

        var source = CurrentPath;
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            return "Der eigene Programmpfad liess sich nicht bestimmen.";
        }

        var messages = new List<string>();

        try
        {
            var alreadyThere = string.Equals(source, TargetPath, StringComparison.OrdinalIgnoreCase);

            if (!alreadyThere)
            {
                Directory.CreateDirectory(TargetDirectory);

                // Sich selbst zu lesen ist erlaubt, auch im Betrieb. Ueber eine
                // bereits laufende Kopie zu schreiben dagegen nicht - deshalb
                // wird ein laufendes Ziel nicht angefasst, sondern gemeldet.
                File.Copy(source, TargetPath, overwrite: true);

                messages.Add($"Kopiert nach {TargetDirectory}");
                relaunchNeeded = true;
            }

            CreateShortcut(DesktopShortcut, TargetPath);
            messages.Add("Verknuepfung auf dem Desktop angelegt");

            CreateShortcut(StartMenuShortcut, TargetPath);
            messages.Add("Im Startmenue eingetragen");
        }
        catch (IOException e)
        {
            return $"Fehlgeschlagen: {e.Message}";
        }
        catch (UnauthorizedAccessException e)
        {
            return $"Keine Berechtigung: {e.Message}";
        }

        return string.Join(". ", messages) + ".";
    }

    /// <summary>
    /// Legt eine .lnk an.
    ///
    /// Ueber den Windows Script Host per COM, weil .NET selbst keine
    /// Verknuepfungen schreiben kann und das Format binaer und undokumentiert
    /// genug ist, um es nicht von Hand nachzubauen. Kein zusaetzliches Paket:
    /// WScript.Shell ist auf jedem Windows vorhanden.
    /// </summary>
    private static void CreateShortcut(string linkPath, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null)
        {
            throw new IOException("WScript.Shell ist auf diesem System nicht verfuegbar.");
        }

        object? shell = null;
        object? link = null;

        try
        {
            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                throw new IOException("WScript.Shell liess sich nicht starten.");
            }

            link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                                     null, shell, [linkPath]);

            if (link is null)
            {
                throw new IOException("Die Verknuepfung liess sich nicht anlegen.");
            }

            var linkType = link.GetType();

            void Set(string name, string value) =>
                linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty,
                                      null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", Path.GetDirectoryName(target) ?? string.Empty);
            Set("Description", "Downgrader und Mod-Installer fuer GTA IV");

            linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            if (link is not null) { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
            if (shell is not null) { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
    }

    /// <summary>Startet die eingerichtete Fassung und beendet diese hier.</summary>
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
