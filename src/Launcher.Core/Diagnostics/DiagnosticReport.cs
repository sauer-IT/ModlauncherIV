using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Diagnostics;

/// <summary>
/// Das vollständige Untersuchungsergebnis: maschinenweite Randbedingungen plus
/// jede gefundene Installation. Einmal erhoben, dann entweder als Klartext oder
/// als JSON ausgegeben — beide Ausgaben zeigen denselben Stand.
/// </summary>
public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAt,
    SystemEnvironment System,
    IReadOnlyList<GameInstall> Installs)
{
    /// <summary>True, wenn irgendetwas eine automatische Behandlung verbietet.</summary>
    public bool HasBlocker =>
        System.Notes.Any(n => n.Level == NoteLevel.Blocker) ||
        Installs.Any(i => i.HasBlocker);
}
