using System.Collections.ObjectModel;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>A target version to pick from.</summary>
public sealed class VersionChoice(string raw, string label, string reason, bool isCurrent)
{
    public string Raw { get; } = raw;

    public string Label { get; } = label;

    public string Reason { get; } = reason;

    public bool IsCurrent { get; } = isCurrent;
}

/// <summary>A recipe with a tick box.</summary>
public sealed class RecipeChoice(Recipe recipe, bool installed) : Observable
{
    private bool _selected;

    public Recipe Recipe { get; } = recipe;

    public string Name => Recipe.Name;

    public string Description => Recipe.Description ?? string.Empty;

    public bool Installed { get; } = installed;

    public string State => Installed ? "already installed" : string.Empty;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

public sealed class ChoiceStep(Session session) : WizardStep(session)
{
    private VersionChoice? _target;

    public override string Title => "What should happen?";

    public override string Lead =>
        "Pick the game version and the mods. The wizard adds dependencies by "
        + "itself in the next step — you do not have to know what an ASI loader is.";

    public ObservableCollection<VersionChoice> Versions { get; } = [];

    public ObservableCollection<RecipeChoice> Recipes { get; } = [];

    public VersionChoice? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value))
            {
                Session.TargetVersion = value?.Raw;
                NotifyChanged();
            }
        }
    }

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

        foreach (var version in KnownVersions.ModdingTargets)
        {
            if (string.Equals(version.Raw, current.Raw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Versions.Add(new VersionChoice(
                version.Raw,
                $"switch to {version.Raw}",
                version.DisplayName,
                isCurrent: false));
        }

        // Preselected is the usual wish: the version with the most mods for it.
        // Anyone wanting something else clicks elsewhere.
        Target = Versions.FirstOrDefault(v => v.Raw == "1.0.7.0") ?? Versions.FirstOrDefault();
    }

    private void BuildRecipes()
    {
        // Do not throw the selection away when going back.
        var previously = Recipes.Where(r => r.Selected).Select(r => r.Recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (previously.Count == 0)
        {
            previously = Session.Wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var ledger = Session.Ledger;

        Recipes.Clear();

        foreach (var recipe in Session.SelectableRecipes)
        {
            Recipes.Add(new RecipeChoice(recipe, ledger.IsInstalled(recipe.Id))
            {
                Selected = previously.Contains(recipe.Id),
            });
        }
    }
}
