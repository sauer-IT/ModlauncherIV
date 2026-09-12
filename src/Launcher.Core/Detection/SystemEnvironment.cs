using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Zustand von Smart App Control. Die Richtlinie wird reputationsbasiert
/// durchgesetzt: unsignierte Binaries ohne Ruf werden blockiert, dieselbe Datei
/// kann aber Minuten später durchgehen. Deshalb ist "Enforced" eine Warnung und
/// keine Gewissheit.
/// </summary>
public enum SmartAppControlState
{
    /// <summary>Kein Wert in der Registry — ältere Windows-Version.</summary>
    NotPresent,

    Off,

    /// <summary>Aktiv. Blockiert unsignierte DLLs, auch in fremden Prozessen.</summary>
    Enforced,

    /// <summary>Evaluierungsmodus. Kann jederzeit in Enforced umschlagen.</summary>
    Evaluation,

    /// <summary>Wert vorhanden, aber unbekannt.</summary>
    Unknown,
}

/// <summary>
/// Maschinenweite Randbedingungen. Gehören nicht zu einer einzelnen Installation,
/// entscheiden aber darüber, ob Rezepte überhaupt laufen können.
/// </summary>
public sealed record SystemEnvironment(
    string OperatingSystem,
    bool IsElevated,
    SmartAppControlState SmartAppControl,
    IReadOnlyList<Note> Notes);

public static class SystemEnvironmentProbe
{
    private const string CiPolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
    private const string SacValue = "VerifiedAndReputablePolicyState";

    public static SystemEnvironment Probe()
    {
        var notes = new List<Note>();

        var sac = ReadSmartAppControl();
        var elevated = IsElevated();

        AddSmartAppControlNotes(sac, notes);

        if (!elevated)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Der Launcher läuft ohne Administratorrechte.",
                "Zum Lesen reicht das. Sobald Rezepte in Program Files schreiben, werden erhöhte Rechte gebraucht."));
        }

        return new SystemEnvironment(
            OperatingSystem: RuntimeInformation.OSDescription,
            IsElevated: elevated,
            SmartAppControl: sac,
            Notes: notes);
    }

    private static void AddSmartAppControlNotes(SmartAppControlState state, List<Note> notes)
    {
        switch (state)
        {
            case SmartAppControlState.Enforced:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Smart App Control ist aktiv.",
                    "Es blockiert unsignierte DLLs — auch solche, die in GTAIV.exe geladen werden. "
                    + "Ein Trainer ist genau das. Die Durchsetzung ist reputationsbasiert und damit "
                    + "nicht vorhersehbar: dieselbe Datei kann beim einen Start blockiert werden und "
                    + "beim nächsten nicht."));

                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control kennt keine Ausnahmeliste.",
                    "Anders als beim Defender lässt sich kein Ordner freigeben. Abschalten wirkt sofort, "
                    + "ist laut Microsoft aber nicht ohne Windows-Neuinstallation umkehrbar."));
                break;

            case SmartAppControlState.Evaluation:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Smart App Control ist im Evaluierungsmodus.",
                    "Windows entscheidet selbst, wann es scharf schaltet. Bis dahin laufen unsignierte "
                    + "Mods, danach womöglich nicht mehr."));
                break;

            case SmartAppControlState.Off:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control ist aus — unsignierte Mods werden nicht blockiert."));
                break;

            case SmartAppControlState.Unknown:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control meldet einen unbekannten Zustand.",
                    "Die Richtlinie könnte unsignierte Mods blockieren."));
                break;

            case SmartAppControlState.NotPresent:
            default:
                break;
        }
    }

    private static SmartAppControlState ReadSmartAppControl()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CiPolicyKey);
            if (key?.GetValue(SacValue) is not int value)
            {
                return SmartAppControlState.NotPresent;
            }

            return value switch
            {
                0 => SmartAppControlState.Off,
                1 => SmartAppControlState.Enforced,
                2 => SmartAppControlState.Evaluation,
                _ => SmartAppControlState.Unknown,
            };
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return SmartAppControlState.Unknown;
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
