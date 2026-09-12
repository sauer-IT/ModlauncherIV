namespace ModlauncherIV.Core.Detection;

/// <summary>Where the installation came from. Decides how updates get locked.</summary>
public enum GamePlatform
{
    Unknown,
    RockstarLauncher,
    Steam,
    Epic,
    Retail,
}

/// <summary>Which game a recipe targets. v1 only ever fills in GtaIV.</summary>
public enum GameTitle
{
    GtaIV,
    Eflc,
}

/// <summary>Kind of foreign file found in the game directory.</summary>
public enum ModArtifactKind
{
    /// <summary>Proxy DLL that loads ASI plugins (dsound, dinput8, d3d9 …).</summary>
    AsiLoader,

    /// <summary>An ASI plugin.</summary>
    AsiPlugin,

    /// <summary>Alexander Blade's ScriptHook.</summary>
    ScriptHook,

    /// <summary>ScriptHookDotNet, for .NET scripts.</summary>
    ScriptHookDotNet,

    /// <summary>GFWL stub (xliveless), or the real xlive.dll.</summary>
    Xlive,

    /// <summary>Script folder belonging to a mod framework.</summary>
    ScriptFolder,

    /// <summary>Recognised as foreign, but not identified.</summary>
    Unknown,
}

/// <summary>How urgent a note in the diagnostic report is.</summary>
public enum NoteLevel
{
    Info,
    Warning,
    Blocker,
}

/// <summary>Something about the installation the user needs to know.</summary>
/// <param name="Level">How urgent.</param>
/// <param name="Message">What was found.</param>
/// <param name="Detail">Optional: what follows from it.</param>
public sealed record Note(NoteLevel Level, string Message, string? Detail = null);

/// <summary>A file in the game directory that did not come from Rockstar.</summary>
public sealed record ModArtifact(
    string RelativePath,
    ModArtifactKind Kind,
    long SizeBytes);

/// <summary>
/// Describes a known game version. <see cref="IsKnown"/> is false when the
/// version found is not listed in <see cref="KnownVersions"/> — and then no
/// recipe may run automatically.
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
        DisplayName: "unknown version",
        IsCompleteEdition: false,
        RequiresGfwl: false,
        IsModdingTarget: false,
        IsKnown: false);
}

/// <summary>A candidate the locator found, before it was inspected.</summary>
public sealed record InstallCandidate(
    string Path,
    GamePlatform Platform,
    string FoundVia);

/// <summary>The complete result of inspecting one installation.</summary>
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
    /// <summary>True when no foreign files were found at all.</summary>
    public bool IsVanilla => ModArtifacts.Count == 0;

    /// <summary>True when something rules out handling this automatically.</summary>
    public bool HasBlocker => Notes.Any(n => n.Level == NoteLevel.Blocker);
}
