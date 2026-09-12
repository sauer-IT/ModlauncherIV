using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

/// <summary>
/// Was der Assistent über den Verlauf hinweg weiß.
///
/// Ein einzelnes Objekt statt Eigenschaften, die von Schritt zu Schritt gereicht
/// werden: die Schritte lesen und schreiben dieselbe Wahrheit, und ein Rücksprung
/// führt nicht zu zwei Ständen, die sich widersprechen.
/// </summary>
public sealed class Session
{
    public GameInstall? Install { get; set; }

    /// <summary>Alles, was die Erkennung beim Start gefunden hat.</summary>
    public IReadOnlyList<GameInstall> Found { get; set; } = [];

    public SystemEnvironment? Environment { get; set; }

    public CatalogLoadResult? Catalog { get; set; }

    /// <summary>Gewünschte Spielversion. Null = die vorhandene behalten.</summary>
    public string? TargetVersion { get; set; }

    /// <summary>Rezepte, die der Nutzer angehakt hat. Ohne Abhängigkeiten.</summary>
    public List<string> Wanted { get; } = [];

    public Journey? Journey { get; set; }

    /// <summary>Rezepte, die tatsächlich gelaufen sind. Für die Schlussseite.</summary>
    public List<string> Applied { get; } = [];

    public string CacheRoot { get; set; } = AppPaths.Cache;

    public InstallLedger Ledger =>
        Install is null ? InstallLedger.Empty(string.Empty) : new LedgerStore(Install.Path).Load();

    /// <summary>Die Rezepte des Katalogs, die für Menschen zur Auswahl taugen.</summary>
    public IReadOnlyList<Recipe> SelectableRecipes =>
        Catalog is null
            ? []
            : Catalog.Recipes
                .Where(r => r.Game == GameTitle.GtaIV && !r.IsVersionTransition)
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .ToArray();
}
