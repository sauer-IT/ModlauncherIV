using System.Text.Json.Serialization;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Catalog;

/// <summary>
/// A file a recipe needs and that is not shipped with it.
/// Without <see cref="Sha256"/> nothing gets installed — that checksum is the
/// only thing standing between a swapped source and arbitrary code landing in
/// the game directory.
/// </summary>
/// <param name="Id">Reference name steps use to address the file.</param>
/// <param name="FileName">File name inside the working directory.</param>
/// <param name="Sha256">Expected checksum, lower case.</param>
/// <param name="SizeBytes">Expected size. Allows refusing before downloading.</param>
/// <param name="Urls">Sources in order. Empty = the user supplies the file.</param>
/// <param name="Note">Hint for the user, e.g. where the file comes from.</param>
public sealed record RecipeSource(
    string Id,
    string FileName,
    string Sha256,
    long SizeBytes,
    IReadOnlyList<string> Urls,
    string? Note = null);

/// <summary>
/// A recipe: one self-contained, reversible change to the game.
///
/// Downgrades are not a special case but recipes with <see cref="ProducesVersion"/>
/// set — which makes them an edge in the version graph.
/// </summary>
public sealed record Recipe(
    string Id,
    string Name,
    string Version,
    GameTitle Game,
    string? Description = null,

    /// <summary>Game versions this recipe may be applied to. Empty = all of them.</summary>
    IReadOnlyList<string>? AppliesToVersions = null,

    /// <summary>Set when the recipe changes the game version. Makes it an edge in the graph.</summary>
    string? ProducesVersion = null,

    /// <summary>Ids of other recipes that must be installed first.</summary>
    IReadOnlyList<string>? Requires = null,

    /// <summary>Ids of recipes that must not be installed at the same time.</summary>
    IReadOnlyList<string>? ConflictsWith = null,

    IReadOnlyList<RecipeSource>? Sources = null,
    IReadOnlyList<RecipeStep>? Steps = null,

    /// <summary>
    /// What kind of thing this is, for sorting the list a user has to read.
    /// Free text: the launcher knows an order for the ones it expects and puts
    /// everything else after them, so a new category needs no code change.
    /// </summary>
    string? Category = null)
{
    public IReadOnlyList<string> AppliesTo => AppliesToVersions ?? [];

    public IReadOnlyList<string> Dependencies => Requires ?? [];

    public IReadOnlyList<string> Conflicts => ConflictsWith ?? [];

    public IReadOnlyList<RecipeSource> RequiredFiles => Sources ?? [];

    public IReadOnlyList<RecipeStep> Actions => Steps ?? [];

    /// <summary>True when the recipe forms an edge in the version graph.</summary>
    [JsonIgnore]
    public bool IsVersionTransition => !string.IsNullOrWhiteSpace(ProducesVersion);

    /// <summary>Checks whether the recipe fits the game version that was found.</summary>
    public bool Matches(string gameVersion) =>
        AppliesTo.Count == 0 ||
        AppliesTo.Any(v => string.Equals(v, gameVersion, StringComparison.OrdinalIgnoreCase));
}
