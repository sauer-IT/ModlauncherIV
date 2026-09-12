using System.IO;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

/// <summary>
/// What the wizard knows across the whole run.
///
/// A single object rather than properties passed from step to step: the steps
/// read and write the same truth, and going back does not produce two states
/// that contradict each other.
/// </summary>
public sealed class Session
{
    public GameInstall? Install { get; set; }

    /// <summary>Everything detection found at startup.</summary>
    public IReadOnlyList<GameInstall> Found { get; set; } = [];

    public SystemEnvironment? Environment { get; set; }

    public CatalogLoadResult? Catalog { get; set; }

    /// <summary>Wanted game version. Null = keep the one in place.</summary>
    public string? TargetVersion { get; set; }

    /// <summary>Recipes the user ticked. Without dependencies.</summary>
    public List<string> Wanted { get; } = [];

    public Journey? Journey { get; set; }

    /// <summary>Recipes that actually ran. For the final page.</summary>
    public List<string> Applied { get; } = [];

    public string CacheRoot { get; set; } = AppPaths.Cache;

    /// <summary>
    /// Reads the installation again, from the files.
    ///
    /// Detection runs once at startup, and its answer used to stand for the rest
    /// of the session - which is wrong the moment anything changes the game.
    /// Take back the downgrade on the home page and the version on disk is the
    /// Complete Edition again, while every page still said 1.0.7.0 and offered
    /// what fits it. The only way out was to close the program and open it
    /// again, which is a thing no user should ever be told to do.
    ///
    /// Only the version and what is in the directory are re-read. Which
    /// installation was chosen is the user's decision and stays theirs.
    /// </summary>
    public void Reinspect()
    {
        if (Install is not { } install)
        {
            return;
        }

        // A game that is not there cannot be read, and what was known about it
        // stays known: an external disk unplugged should leave the page out of
        // date, not blank.
        if (!File.Exists(install.ExecutablePath))
        {
            return;
        }

        var fresh = new InstallInspector().Inspect(
            new InstallCandidate(install.Path, install.Platform, install.FoundVia));

        Install = fresh;

        Found = Found
            .Select(f => string.Equals(f.Path, fresh.Path, StringComparison.OrdinalIgnoreCase) ? fresh : f)
            .ToArray();
    }

    public InstallLedger Ledger =>
        Install is null ? InstallLedger.Empty(string.Empty) : new LedgerStore(Install.Path).Load();

    /// <summary>The catalog recipes that make sense for a human to pick.</summary>
    public IReadOnlyList<Recipe> SelectableRecipes =>
        Catalog is null
            ? []
            : Catalog.Recipes
                .Where(r => r.Game == GameTitle.GtaIV && !r.IsVersionTransition)
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .ToArray();
}
