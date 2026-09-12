using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Verification;

public enum OwnedFileState
{
    Unchanged,

    /// <summary>Existiert noch, hat aber einen anderen Inhalt als beim Einbau.</summary>
    Modified,

    /// <summary>Ist verschwunden.</summary>
    Missing,

    /// <summary>Beim Einbau wurde keine Prüfsumme festgehalten.</summary>
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
    /// Beide Seiten werden vorher auf die kanonische Schreibweise gebracht:
    /// "1, 0, 7, 0" und "1.0.7.0" sind dieselbe Version, und ein Fehlalarm hier
    /// wuerde die Warnung entwerten, um die es eigentlich geht.
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
/// Vergleicht, was der Launcher eingebaut hat, mit dem, was tatsächlich da liegt.
///
/// Das ist die Gegenprobe zur Update-Sperre. Steam und der Rockstar Launcher
/// können eine Installation jederzeit zurücksetzen — meist unbemerkt, und der
/// Nutzer merkt es erst daran, dass seine Mods stumm bleiben. Hier fällt es auf,
/// und zwar mit Namen: welche Datei, aus welchem Rezept.
/// </summary>
public static class InstallVerifier
{
    public static VerificationResult Verify(GameInstall install, InstallLedger ledger)
    {
        var files = new List<VerifiedFile>();
        var notes = new List<Note>();

        foreach (var entry in ledger.Entries)
        {
            foreach (var owned in entry.Files)
            {
                files.Add(new VerifiedFile(
                    entry.RecipeId,
                    owned.RelativePath,
                    Inspect(install.Path, owned)));
            }
        }

        // Die zuletzt festgehaltene Version ist die, die wir erwarten. Weicht die
        // tatsaechliche ab, hat jemand anders am Spiel gearbeitet als wir.
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
                "Der Launcher hat an dieser Installation nichts eingebaut — nichts zu prüfen."));
            return;
        }

        if (result.VersionReverted)
        {
            notes.Add(new Note(
                NoteLevel.Blocker,
                $"Die Spielversion ist {result.CurrentVersion}, erwartet war {result.ExpectedVersion}.",
                "Die Plattform hat das Spiel zurückgesetzt. Ein Downgrade ist damit hinfällig, "
                + "und darauf aufbauende Mods laufen nicht mehr."));
        }

        if (result.MissingCount > 0)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{result.MissingCount} eingebaute Datei(en) fehlen.",
                "Entweder hat die Plattform sie entfernt, oder sie wurden von Hand gelöscht."));
        }

        if (result.ModifiedCount > 0)
        {
            notes.Add(new Note(
                NoteLevel.Warning,
                $"{result.ModifiedCount} eingebaute Datei(en) wurden verändert.",
                "Ein Update oder eine Dateiprüfung der Plattform überschreibt genau so."));
        }

        if (result.IsIntact)
        {
            notes.Add(new Note(
                NoteLevel.Info,
                "Alles unverändert — die Installation ist so, wie der Launcher sie hinterlassen hat."));
        }
    }
}
