namespace ModlauncherIV.Core.Detection;

/// <summary>Woher die Installation stammt. Bestimmt, wie das Update gesperrt wird.</summary>
public enum GamePlatform
{
    Unknown,
    RockstarLauncher,
    Steam,
    Epic,
    Retail,
}

/// <summary>Welches Spiel ein Rezept betrifft. v1 befüllt nur GtaIV.</summary>
public enum GameTitle
{
    GtaIV,
    Eflc,
}

/// <summary>Art einer gefundenen Fremddatei im Spielverzeichnis.</summary>
public enum ModArtifactKind
{
    /// <summary>Proxy-DLL, die ASI-Plugins nachlädt (dsound, dinput8, d3d9 …).</summary>
    AsiLoader,

    /// <summary>Ein ASI-Plugin.</summary>
    AsiPlugin,

    /// <summary>Alexander Blades ScriptHook.</summary>
    ScriptHook,

    /// <summary>ScriptHookDotNet für .NET-Skripte.</summary>
    ScriptHookDotNet,

    /// <summary>GFWL-Stub (xliveless) oder das echte xlive.dll.</summary>
    Xlive,

    /// <summary>Skriptordner eines Mod-Frameworks.</summary>
    ScriptFolder,

    /// <summary>Erkannt, aber nicht zugeordnet.</summary>
    Unknown,
}

/// <summary>Schweregrad einer Anmerkung im Diagnosebericht.</summary>
public enum NoteLevel
{
    Info,
    Warning,
    Blocker,
}

/// <summary>Eine Feststellung über die Installation, die der Nutzer wissen muss.</summary>
/// <param name="Level">Wie dringend.</param>
/// <param name="Message">Was festgestellt wurde.</param>
/// <param name="Detail">Optional: was daraus folgt.</param>
public sealed record Note(NoteLevel Level, string Message, string? Detail = null);

/// <summary>Eine im Spielverzeichnis gefundene Datei, die nicht von Rockstar stammt.</summary>
public sealed record ModArtifact(
    string RelativePath,
    ModArtifactKind Kind,
    long SizeBytes);

/// <summary>
/// Beschreibt eine bekannte Spielversion. <see cref="IsKnown"/> ist false,
/// wenn die gefundene Version nicht in <see cref="KnownVersions"/> steht — dann
/// darf kein Rezept automatisch laufen.
/// </summary>
public sealed record GameVersionInfo(
    string Raw,
    Version? Parsed,
    string DisplayName,
    bool IsCompleteEdition,
    bool RequiresGfwl,
    bool IsModdingTarget,
    bool IsKnown)
{
    public static GameVersionInfo Unrecognised(string raw) => new(
        Raw: raw,
        Parsed: Version.TryParse(raw, out var v) ? v : null,
        DisplayName: "unbekannte Version",
        IsCompleteEdition: false,
        RequiresGfwl: false,
        IsModdingTarget: false,
        IsKnown: false);
}

/// <summary>Ein Kandidat, den der Locator gefunden hat, bevor er untersucht wurde.</summary>
public sealed record InstallCandidate(
    string Path,
    GamePlatform Platform,
    string FoundVia);

/// <summary>Das vollständige Untersuchungsergebnis einer Installation.</summary>
public sealed record GameInstall(
    string Path,
    GamePlatform Platform,
    string FoundVia,
    GameVersionInfo Version,
    string ExecutablePath,
    string? ExecutableSha256,
    long ExecutableSizeBytes,
    bool HasTlad,
    bool HasTbogt,
    IReadOnlyList<ModArtifact> ModArtifacts,
    IReadOnlyList<Note> Notes)
{
    /// <summary>True, wenn keinerlei Fremddateien gefunden wurden.</summary>
    public bool IsVanilla => ModArtifacts.Count == 0;

    /// <summary>True, wenn irgendetwas eine automatische Behandlung verbietet.</summary>
    public bool HasBlocker => Notes.Any(n => n.Level == NoteLevel.Blocker);
}
