using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace ModlauncherIV.Core.Detection;

/// <summary>
/// State of Smart App Control. The policy is enforced by reputation: unsigned
/// binaries without a reputation get blocked, yet the same file may pass minutes
/// later. That is why "Enforced" is a warning and not a certainty.
/// </summary>
public enum SmartAppControlState
{
    /// <summary>No value in the registry — an older Windows version.</summary>
    NotPresent,

    Off,

    /// <summary>Active. Blocks unsigned DLLs, including inside other processes.</summary>
    Enforced,

    /// <summary>Evaluation mode. Can flip to Enforced at any time.</summary>
    Evaluation,

    /// <summary>Value present, but not recognised.</summary>
    Unknown,
}

/// <summary>
/// Machine-wide conditions. They do not belong to a single installation, but
/// they decide whether recipes can run at all.
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
                "The launcher is running without administrator rights.",
                "That is enough for reading. As soon as recipes write into Program Files, elevation is needed."));
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
                    "Smart App Control is active.",
                    "It blocks unsigned DLLs — including the ones loaded into GTAIV.exe. "
                    + "A trainer is exactly that. Enforcement is reputation-based and therefore "
                    + "unpredictable: the same file can be blocked on one start and not on the next."));

                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control has no exclusion list.",
                    "Unlike Defender, no folder can be exempted. Turning it off takes effect immediately "
                    + "but, according to Microsoft, cannot be undone without reinstalling Windows."));
                break;

            case SmartAppControlState.Evaluation:
                notes.Add(new Note(
                    NoteLevel.Warning,
                    "Smart App Control is in evaluation mode.",
                    "Windows decides by itself when to switch it on. Until then unsigned mods run, "
                    + "afterwards they may not."));
                break;

            case SmartAppControlState.Off:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control is off — unsigned mods will not be blocked."));
                break;

            case SmartAppControlState.Unknown:
                notes.Add(new Note(
                    NoteLevel.Info,
                    "Smart App Control reports a state we do not recognise.",
                    "The policy might block unsigned mods."));
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
