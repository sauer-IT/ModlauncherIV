using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Verification;

public enum OwnedFileState
{
    Unchanged,

    /// <summary>Still there, but with different content than at install time.</summary>
    Modified,

    /// <summary>Gone.</summary>
    Missing,

    /// <summary>No checksum was recorded at install time.</summary>
    Unverifiable,
}

public sealed record VerifiedFile(string RecipeId, string RelativePath, OwnedFileState State);

public sealed record VerificationResult(
    string GameRoot,
    string? CurrentVersion,
    string? ExpectedVersion,
    IReadOnlyList<VerifiedFile> Files,
    IReadOnlyList<Note> Notes)
{
    public int ModifiedCount => Files.Count(f => f.State == OwnedFileState.Modified);

    public int MissingCount => Files.Count(f => f.State == OwnedFileState.Missing);

    /// <summary>
    /// Both sides are brought into canonical spelling first: "1, 0, 7, 0" and
    /// "1.0.7.0" are the same version, and a false alarm here would devalue the
    /// very warning this is about.
    /// </summary>
    public bool VersionReverted =>
        ExpectedVersion is not null &&
        CurrentVersion is not null &&
        !string.Equals(
            KnownVersions.Normalise(ExpectedVersion),
            KnownVersions.Normalise(CurrentVersion),
            StringComparison.OrdinalIgnoreCase);

    public bool IsIntact => ModifiedCount == 0 && MissingCount == 0 && !VersionReverted;
}

/// <summary>
/// Compares what the launcher installed against what is actually on disk.
///
/// This is the counter-check to the update guard. Steam and the Rockstar
/// Launcher can reset an installation at any time — usually unnoticed, and the
/// user only realises it because the mods stay silent. Here it shows up, and by
/// name: which file, from which recipe.
/// </summary>
public static class InstallVerifier
{
    public static VerificationResult Verify(GameInstall install, InstallLedger ledger)
    {
        var files = new List<VerifiedFile>();
        var notes = new List<Note>();

        // A path can be owned by more than one recipe. FusionFix ships its own
        // dinput8.dll over the one the ASI loader installed, and both entries
        // are honest about what they wrote - but only the newer one still
        // describes what is on disk.
        //
        // Checking the older record as well would report a change the launcher
        // made itself, knowingly, on every single run. The warning that matters
        // here - the platform quietly resetting the game - would then sit in the
        // middle of noise nobody reads any more.
        var owners = ledger.Entries
            .OrderBy(e => e.InstalledAt)
            .SelectMany(e => e.Files.Select(f => (Entry: e, File: f)))
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

        foreach (var (entry, owned) in owners)
        {
            files.Add(new VerifiedFile(
                entry.RecipeId,
                owned.RelativePath,
                Inspect(install.Path, owned)));
        }

        // The version recorded last is the one we expect. If the actual one differs,
        // somebody other than us has been working on the game.
        var expected = ledger.Entries
            .Where(e => e.GameVersionAfter is not null)
            .OrderByDescending(e => e.InstalledAt)
            .Select(e => e.GameVersionAfter)
            .FirstOrDefault();

        var current = install.Version.IsKnown || install.Version.Raw.Length > 0
            ? install.Version.Raw
            : null;

        var result = new VerificationResult(install.Path, current, expected, files, notes);

        AddInterpretation(result, notes);
        return result with { Notes = notes };
    }

    private static OwnedFileState Inspect(string gameRoot, OwnedFile owned)
    {
        var path = Path.Combine(gameRoot, owned.RelativePath);

        if (!File.Exists(path))
        {
            return OwnedFileState.Missing;
        }

        if (owned.Sha256 is null)
        {
            return OwnedFileState.Unverifiable;
        }

        try
        {
            return Hashing.Equal(Hashing.Sha256File(path), owned.Sha256)
                ? OwnedFileState.Unchanged
                : OwnedFileState.Modified;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return OwnedFileState.Unverifiable;
        }
    }

    private static void AddInterpretation(VerificationResult result, List<Note> notes)
    {
        if (result.Files.Count == 0)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "The launcher has installed nothing here — nothing to check."));
            return;
        }

        if (result.VersionReverted)
        {
            notes.Add(new Note(
                NoteLevel.Blocker,
                $"The game version is {result.CurrentVersion}, {result.ExpectedVersion} was expected.",
                "The platform reset the game. That undoes the downgrade, "
                + "and mods building on it no longer run."));
        }

        if (result.MissingCount > 0)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{result.MissingCount} installed file(s) are missing.",
                "Either the platform removed them, or they were deleted by hand."));
        }

        if (result.ModifiedCount > 0)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{result.ModifiedCount} installed file(s) have been changed.",
                "A platform update or file verification overwrites exactly like this."));
        }

        if (result.IsIntact)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Everything unchanged — the installation is as the launcher left it."));
        }
    }
}
