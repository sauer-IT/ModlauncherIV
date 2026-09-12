using System.Collections.ObjectModel;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>Eine Zielversion zur Auswahl.</summary>
public sealed class VersionChoice(string raw, string label, string reason, bool isCurrent)
{
    public string Raw { get; } = raw;

    public string Label { get; } = label;

    public string Reason { get; } = reason;

    public bool IsCurrent { get; } = isCurrent;
}

/// <summary>Ein ankreuzbares Rezept.</summary>
public sealed class RecipeChoice(Recipe recipe, bool installed) : Observable
{
    private bool _selected;

    public Recipe Recipe { get; } = recipe;

    public string Name => Recipe.Name;

    public string Description => Recipe.Description ?? string.Empty;

    public bool Installed { get; } = installed;

    public string State => Installed ? "bereits installiert" : string.Empty;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

public sealed class ChoiceStep(Session session) : WizardStep(session)
{
    private VersionChoice? _target;

    public override string Title => "Was soll passieren?";

    public override string Lead =>
        "Wähle die Spielversion und die Mods. Abhängigkeiten ergänzt der Assistent "
        + "im nächsten Schritt von selbst — du musst nicht wissen, was ein ASI-Loader ist.";

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
    /// Weiter geht es nur mit einem Ziel. Nichts auszuwählen ist erlaubt — dann
    /// bleibt es beim reinen Versionswechsel, was ein völlig legitimer Wunsch ist.
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

        // Die vorhandene Version steht immer zur Wahl: wer nur eine Mod einbauen
        // will, soll dafür nicht sein Spiel herunterstufen müssen.
        Versions.Add(new VersionChoice(
            current.Raw,
            current.IsKnown ? $"{current.Raw} behalten" : "Version behalten",
            current.IsKnown
                ? current.DisplayName
                : "Die vorhandene Version konnte nicht zugeordnet werden.",
            isCurrent: true));

        foreach (var version in KnownVersions.ModdingTargets)
        {
            if (string.Equals(version.Raw, current.Raw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Versions.Add(new VersionChoice(
                version.Raw,
                $"auf {version.Raw} wechseln",
                version.DisplayName,
                isCurrent: false));
        }

        // Vorbelegt ist der übliche Wunsch: die Version, für die es die meisten
        // Mods gibt. Wer es anders will, klickt daneben.
        Target = Versions.FirstOrDefault(v => v.Raw == "1.0.7.0") ?? Versions.FirstOrDefault();
    }

    private void BuildRecipes()
    {
        // Die Auswahl beim Zurückspringen nicht wegwerfen.
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
