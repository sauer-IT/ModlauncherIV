using System.Text.Json.Serialization;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Catalog;

/// <summary>
/// Eine Datei, die ein Rezept braucht und die nicht mitgeliefert wird.
/// Ohne <see cref="Sha256"/> wird nichts installiert — das ist der einzige Schutz
/// davor, dass eine ausgetauschte Quelle beliebigen Code ins Spiel schreibt.
/// </summary>
/// <param name="Id">Referenzname, unter dem Schritte die Datei ansprechen.</param>
/// <param name="FileName">Dateiname im Arbeitsverzeichnis.</param>
/// <param name="Sha256">Erwartete Prüfsumme, klein geschrieben.</param>
/// <param name="SizeBytes">Erwartete Größe. Erlaubt eine Absage vor dem Download.</param>
/// <param name="Urls">Bezugsquellen in Reihenfolge. Leer = der Nutzer legt die Datei selbst ab.</param>
/// <param name="Note">Hinweis für den Nutzer, etwa wo die Datei herkommt.</param>
public sealed record RecipeSource(
    string Id,
    string FileName,
    string Sha256,
    long SizeBytes,
    IReadOnlyList<string> Urls,
    string? Note = null);

/// <summary>
/// Ein Rezept: eine abgeschlossene, umkehrbare Änderung am Spiel.
///
/// Downgrades sind keine Sonderfälle, sondern Rezepte mit gesetztem
/// <see cref="ProducesVersion"/> — sie bilden damit eine Kante im Versionsgraphen.
/// </summary>
public sealed record Recipe(
    string Id,
    string Name,
    string Version,
    GameTitle Game,
    string? Description = null,

    /// <summary>Spielversionen, auf die das Rezept angewendet werden darf. Leer = alle.</summary>
    IReadOnlyList<string>? AppliesToVersions = null,

    /// <summary>Gesetzt, wenn das Rezept die Spielversion ändert. Macht es zur Kante im Graphen.</summary>
    string? ProducesVersion = null,

    /// <summary>IDs anderer Rezepte, die vorher installiert sein müssen.</summary>
    IReadOnlyList<string>? Requires = null,

    /// <summary>IDs von Rezepten, die nicht gleichzeitig installiert sein dürfen.</summary>
    IReadOnlyList<string>? ConflictsWith = null,

    IReadOnlyList<RecipeSource>? Sources = null,
    IReadOnlyList<RecipeStep>? Steps = null)
{
    public IReadOnlyList<string> AppliesTo => AppliesToVersions ?? [];

    public IReadOnlyList<string> Dependencies => Requires ?? [];

    public IReadOnlyList<string> Conflicts => ConflictsWith ?? [];

    public IReadOnlyList<RecipeSource> RequiredFiles => Sources ?? [];

    public IReadOnlyList<RecipeStep> Actions => Steps ?? [];

    /// <summary>True, wenn das Rezept eine Kante im Versionsgraphen bildet.</summary>
    [JsonIgnore]
    public bool IsVersionTransition => !string.IsNullOrWhiteSpace(ProducesVersion);

    /// <summary>Prüft, ob das Rezept auf die gefundene Spielversion passt.</summary>
    public bool Matches(string gameVersion) =>
        AppliesTo.Count == 0 ||
        AppliesTo.Any(v => string.Equals(v, gameVersion, StringComparison.OrdinalIgnoreCase));
}
