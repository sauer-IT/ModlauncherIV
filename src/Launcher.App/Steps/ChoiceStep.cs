using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>A target version to pick from.</summary>
public sealed class VersionChoice(
    string raw,
    string label,
    string reason,
    bool isCurrent,
    bool reachable = true)
{
    public string Raw { get; } = raw;

    public string Label { get; } = label;

    public string Reason { get; } = reason;

    public bool IsCurrent { get; } = isCurrent;

    /// <summary>
    /// False when no recipe leads there from where the game is now.
    ///
    /// Such a version is shown rather than left out: it is the one the mods
    /// underneath keep asking for, and a list that silently lacks the answer
    /// to the question on the screen is worse than one that says why.
    /// </summary>
    public bool Reachable { get; } = reachable;
}

/// <summary>A recipe with a tick box.</summary>
public sealed class RecipeChoice : Observable
{
    private bool _selected;

    public RecipeChoice(Recipe recipe, bool installed, bool available, string? unavailableBecause)
    {
        Recipe = recipe;
        Installed = installed;
        Available = available;
        UnavailableBecause = unavailableBecause ?? string.Empty;
        SizeText = DescribeSize(recipe);
        ByHand = NeedsUserSuppliedFile(recipe);
    }

    public Recipe Recipe { get; }

    public string Name => Recipe.Name;

    public string Description => Recipe.Description ?? string.Empty;

    /// <summary>
    /// The description down to what fits on one line of a list.
    ///
    /// Some of these run to a paragraph - FusionFix explains its own crash on
    /// 1.0.7.0 in the description, and rightly so. In a list of forty that
    /// paragraph is what makes the list unreadable, so the row gets the first
    /// sentence and the whole thing sits in the tooltip.
    /// </summary>
    public string ShortDescription => Shorten(Description);

    public string Category => string.IsNullOrWhiteSpace(Recipe.Category) ? "More" : Recipe.Category!;

    public bool Installed { get; }

    /// <summary>False when the recipe does not fit the version chosen above.</summary>
    public bool Available { get; }

    public string UnavailableBecause { get; }

    /// <summary>Total download, as something a person reads rather than counts.</summary>
    public string SizeText { get; }

    /// <summary>True when at least one file has to be fetched by the user.</summary>
    public bool ByHand { get; }

    public bool Selected
    {
        get => _selected;

        // A recipe that does not fit the chosen version cannot be ticked. The
        // planner would refuse it later anyway, and a tick that quietly turns
        // into an error two pages on is worse than one that does not go in.
        set => Set(ref _selected, value && Available);
    }

    private static string Shorten(string text)
    {
        const int limit = 150;

        if (text.Length <= limit)
        {
            return text;
        }

        // A sentence end, if there is one at a sensible place. "1.0.7.0" has no
        // space after its dots, so version numbers do not cut the line in half.
        var stop = text.IndexOf(". ", StringComparison.Ordinal);
        if (stop is > 40 and < limit)
        {
            return text[..(stop + 1)];
        }

        var space = text.LastIndexOf(' ', limit - 10);
        return text[..(space > 60 ? space : limit - 10)].TrimEnd() + " ...";
    }

    private static string DescribeSize(Recipe recipe)
    {
        var bytes = recipe.RequiredFiles.Sum(s => s.SizeBytes);

        return bytes switch
        {
            <= 0 => string.Empty,
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
        };
    }

    /// <summary>
    /// A file without a URL is either one the launcher carries itself or one
    /// only the user can fetch - Nexus hands out links that expire. The two look
    /// identical in the recipe and are opposites for whoever is standing in
    /// front of the wizard, so the shipped payload decides which it is.
    /// </summary>
    private static bool NeedsUserSuppliedFile(Recipe recipe) => recipe.RequiredFiles.Any(s =>
        s.Urls.Count == 0 && !ShippedWithLauncher(s.FileName));

    private static bool ShippedWithLauncher(string fileName)
    {
        try
        {
            return File.Exists(Path.Combine(AppPaths.BundledDirectory, fileName));
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// One headed block in the mod list.
///
/// The list was flat, and flat works for twelve entries the way a drawer works
/// until it is full. Grouping is the thing that still works at forty.
/// </summary>
public sealed class RecipeGroup(string name, string note, IReadOnlyList<RecipeChoice> items)
{
    public string Name { get; } = name;

    /// <summary>One line telling the reader what the whole group is for.</summary>
    public string Note { get; } = note;

    public IReadOnlyList<RecipeChoice> Items { get; } = items;

    public string Count => Items.Count == 1 ? "1 mod" : $"{Items.Count} mods";
}

public sealed class ChoiceStep(Session session) : WizardStep(session)
{
    /// <summary>
    /// The order the groups are read in, which is the order they matter in -
    /// not the alphabet. Anything the catalog names that is not in here comes
    /// after them, so a new category needs no code change.
    /// </summary>
    private static readonly string[] GroupOrder = ["Foundation", "Fixes", "Visuals", "Trainers"];

    private static readonly Dictionary<string, string> GroupNotes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Foundation"] = "What other mods stand on. Ticking is optional - anything that needs one of these brings it along by itself.",
        ["Fixes"] = "Bugs Rockstar never patched, and what modern hardware needs.",
        ["Visuals"] = "Changes how the game looks. None of it is needed for the game to run.",
        ["Trainers"] = "Menus in the game: spawn vehicles, teleport, change the weather.",
    };

    private VersionChoice? _target;
    private string _filter = string.Empty;

    public override string Title => "What should happen?";

    public override string Lead =>
        "Pick the game version and the mods. The wizard adds dependencies by "
        + "itself in the next step — you do not have to know what an ASI loader is.";

    public ObservableCollection<VersionChoice> Versions { get; } = [];

    /// <summary>Every selectable recipe, regardless of filter or group.</summary>
    public ObservableCollection<RecipeChoice> Recipes { get; } = [];

    /// <summary>What the list actually shows: grouped, filtered, in reading order.</summary>
    public ObservableCollection<RecipeGroup> Groups { get; } = [];

    public VersionChoice? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value))
            {
                Session.TargetVersion = value?.Raw;

                // Which mods fit depends on it, so the list is rebuilt rather
                // than left showing things that no longer apply.
                BuildRecipes();
                NotifyChanged();
            }
        }
    }

    /// <summary>Types into the search box. Narrows by name and description.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value ?? string.Empty))
            {
                Regroup();
            }
        }
    }

    /// <summary>The line under the list saying where one stands.</summary>
    public string Summary
    {
        get
        {
            var selected = Recipes.Count(r => r.Selected);
            var shown = Groups.Sum(g => g.Items.Count);

            if (Recipes.Count == 0)
            {
                return string.Empty;
            }

            var head = selected == 0
                ? "Nothing picked - that is allowed, then only the version changes."
                : selected == 1 ? "1 mod picked." : $"{selected} mods picked.";

            if (shown < Recipes.Count)
            {
                head += $" Showing {shown} of {Recipes.Count}.";
            }

            return head;
        }
    }

    /// <summary>Shown when the filter matches nothing at all.</summary>
    public bool NothingMatches => Recipes.Count > 0 && Groups.Count == 0;

    /// <summary>
    /// You can only move on with a target. Picking nothing is fine — then it
    /// stays a pure version change, which is a perfectly legitimate wish.
    /// </summary>
    public override bool CanGoNext => Target is not null;

    public override Task EnterAsync()
    {
        BuildVersions();
        BuildRecipes();

        return Task.CompletedTask;
    }

    public override Task<bool> LeaveAsync()
    {
        Session.Wanted.Clear();
        Session.Wanted.AddRange(Recipes.Where(r => r.Selected).Select(r => r.Recipe.Id));

        return Task.FromResult(true);
    }

    private void BuildVersions()
    {
        Versions.Clear();

        var current = Session.Install?.Version;
        if (current is null)
        {
            return;
        }

        // The version in place is always on offer: somebody who only wants to add
        // one mod should not have to downgrade their game for it.
        Versions.Add(new VersionChoice(
            current.Raw,
            current.IsKnown ? $"keep {current.Raw}" : "keep the version",
            current.IsKnown
                ? current.DisplayName
                : "The version in place could not be recognised.",
            isCurrent: true));

        // What is on offer comes from the catalog, not from a list of versions
        // that exist: a version no recipe produces is not a choice, it is a
        // dead end with a nice name. 1.0.4.0 is exactly that today.
        var recipes = Session.Catalog?.Recipes ?? [];
        var graph = VersionGraph.Build(recipes);
        var reachable = graph.ReachableFrom(current.Raw);

        var produced = recipes
            .Where(r => r.IsVersionTransition && r.Game == GameTitle.GtaIV)
            .Select(r => r.ProducesVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(v => !string.Equals(v, current.Raw, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => KnownVersions.Resolve(v).Parsed);

        foreach (var version in produced)
        {
            var info = KnownVersions.Resolve(version);

            // With an unrecognised version in place, nothing can be said about
            // what is reachable - and greying everything out would strand the
            // user here. The planner says it properly one page on, where the
            // unknown version is its own finding rather than a consequence.
            var canGetThere = !current.IsKnown
                              || reachable.Contains(version, StringComparer.OrdinalIgnoreCase);

            Versions.Add(new VersionChoice(
                version,
                canGetThere ? $"switch to {version}" : $"{version} - not from here",
                canGetThere ? info.DisplayName : WhyNot(version, current.Raw),
                isCurrent: false,
                reachable: canGetThere));
        }

        // Preselected is the usual wish: the version with the most mods for it.
        // Anyone wanting something else clicks elsewhere.
        Target = Versions.FirstOrDefault(v => v is { Raw: "1.0.7.0", Reachable: true })
                 ?? Versions.FirstOrDefault(v => v.Reachable);
    }

    /// <summary>
    /// Why a version cannot be had from where the game stands - and, where
    /// there is one, the way to it anyway.
    ///
    /// The usual case is somebody on 1.0.7.0 looking at four mods that want
    /// 1.0.8.0. There is no edge between the two downgrades and there should
    /// not be one: both start from the Complete Edition, and the way back to it
    /// is the snapshot the downgrade took. That is one click on the home page,
    /// so it is worth naming rather than leaving as "no path".
    /// </summary>
    private string WhyNot(string wanted, string current)
    {
        var back = Session.Catalog?.Recipes.FirstOrDefault(r =>
            r.IsVersionTransition
            && string.Equals(r.ProducesVersion, current, StringComparison.OrdinalIgnoreCase)
            && Session.Ledger.IsInstalled(r.Id));

        if (back is not null)
        {
            return $"The game is on {current} through \"{back.Name}\". Remove that on the home page - "
                   + $"it puts the original version back from its snapshot - and {wanted} is one step from there.";
        }

        return $"No recipe leads from {current} to {wanted}.";
    }

    private void BuildRecipes()
    {
        // Do not throw the selection away when going back, or when the version
        // above is changed.
        var previously = Recipes.Where(r => r.Selected).Select(r => r.Recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (previously.Count == 0)
        {
            previously = Session.Wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var ledger = Session.Ledger;
        var version = Target?.Raw;

        foreach (var old in Recipes)
        {
            old.PropertyChanged -= OnChoiceChanged;
        }

        Recipes.Clear();

        foreach (var recipe in Session.SelectableRecipes)
        {
            var fits = version is null || recipe.Matches(version);

            var choice = new RecipeChoice(
                recipe,
                ledger.IsInstalled(recipe.Id),
                fits,
                fits ? null : $"needs {string.Join(" or ", recipe.AppliesTo)}")
            {
                Selected = previously.Contains(recipe.Id),
            };

            choice.PropertyChanged += OnChoiceChanged;
            Recipes.Add(choice);
        }

        Regroup();
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecipeChoice.Selected))
        {
            Raise(nameof(Summary));
        }
    }

    /// <summary>
    /// Puts the list together as it is shown: filtered, grouped, and with
    /// everything the chosen version cannot take collected at the end rather
    /// than scattered through it.
    /// </summary>
    private void Regroup()
    {
        Groups.Clear();

        var matching = Recipes.Where(Matches).ToArray();

        foreach (var name in matching
                     .Where(r => r.Available)
                     .Select(r => r.Category)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(Rank)
                     .ThenBy(n => n, StringComparer.CurrentCulture))
        {
            var items = matching
                .Where(r => r.Available && string.Equals(r.Category, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .ToArray();

            Groups.Add(new RecipeGroup(
                name.ToUpperInvariant(),
                GroupNotes.GetValueOrDefault(name, string.Empty),
                items));
        }

        var unavailable = matching.Where(r => !r.Available)
            .OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .ToArray();

        if (unavailable.Length > 0)
        {
            Groups.Add(new RecipeGroup(
                $"NOT FOR {Target?.Raw}",
                "These need a different game version. It is picked at the top of this page, which "
                + "also says whether the game can get there from where it stands.",
                unavailable));
        }

        Raise(nameof(Summary));
        Raise(nameof(NothingMatches));
    }

    private bool Matches(RecipeChoice choice)
    {
        if (string.IsNullOrWhiteSpace(_filter))
        {
            return true;
        }

        var needle = _filter.Trim();

        return choice.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || choice.Category.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || choice.Description.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int Rank(string category)
    {
        var index = Array.FindIndex(GroupOrder, g => string.Equals(g, category, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? GroupOrder.Length : index;
    }
}
