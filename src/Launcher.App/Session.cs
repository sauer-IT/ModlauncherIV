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
