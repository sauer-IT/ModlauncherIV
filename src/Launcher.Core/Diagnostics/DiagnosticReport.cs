using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Diagnostics;

/// <summary>
/// The complete inspection result: machine-wide conditions plus every
/// installation found. Gathered once, then rendered either as plain text or as
/// JSON — both outputs show the same state.
/// </summary>
public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAt,
    SystemEnvironment System,
    IReadOnlyList<GameInstall> Installs)
{
    /// <summary>True when anything rules out handling this automatically.</summary>
    public bool HasBlocker =>
        System.Notes.Any(n => n.Level == NoteLevel.Blocker) ||
        Installs.Any(i => i.HasBlocker);
}
