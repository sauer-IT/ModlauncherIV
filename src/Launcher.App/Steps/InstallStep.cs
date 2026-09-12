using System.Collections.ObjectModel;
using System.IO;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>An installation that was found, as it appears in the list.</summary>
public sealed class InstallChoice(GameInstall install)
{
    public GameInstall Install { get; } = install;

    public string Path => Install.Path;

    public string Platform => Install.Platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Disc",
        _ => "unknown origin",
    };

    public string Version => Install.Version.IsKnown
        ? $"{Install.Version.Raw} — {Install.Version.DisplayName}"
        : $"{Install.Version.Raw} (not recognised)";

    public string Extras => Install switch
    {
        { HasTlad: true, HasTbogt: true } => "with both episodes",
        { HasTlad: true } => "with The Lost and Damned",
        { HasTbogt: true } => "with The Ballad of Gay Tony",
        _ => "without episodes",
    };

    public string Mods => Install.ModArtifacts.Count == 0
        ? "unchanged"
        : $"{Install.ModArtifacts.Count} foreign file(s) found";
}

public sealed class InstallStep(Session session) : WizardStep(session)
{
    private InstallChoice? _selected;
    private string? _error;
    private bool _searched;

    public override string Title => "Installation";

    public override string Lead =>
        "The wizard searches for GTA IV in the registry, in Steam and Epic, and in "
        + "the usual places. If yours is not among them, point it at the folder.";

    public ObservableCollection<InstallChoice> Found { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public InstallChoice? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Session.Install = value?.Install;
                NotifyChanged();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public bool NothingFound => _searched && Found.Count == 0;

    public override bool CanGoNext => Selected is not null;

    public override async Task EnterAsync()
    {
        if (_searched)
        {
            return;
        }

        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Takes in a manually chosen folder. Called from the window after the user
    /// confirmed in the file dialog.
    /// </summary>
    public void AddManually(string path)
    {
        Error = null;

        if (!File.Exists(Path.Combine(path, InstallInspector.ExecutableName)))
        {
            Error = $"There is no {InstallInspector.ExecutableName} in {path}.";
            return;
        }

        if (Found.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Selected = Found.First(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var locator = new InstallLocator();
        var candidate = new InstallCandidate(path, locator.InferPlatform(path), "given by hand");
        var choice = new InstallChoice(new InstallInspector().Inspect(candidate));

        Found.Add(choice);
        Selected = choice;

        Raise(nameof(NothingFound));
    }

    private async Task SearchAsync()
    {
        Error = null;
        Found.Clear();
        Warnings.Clear();

        // The shell already searched at startup. Searching again here would not
        // only be slow, it could also produce a different answer than the one the
        // home page just displayed.
        if (Session.Found.Count == 0)
        {
            await Detection.FillAsync(Session).ConfigureAwait(true);
        }

        foreach (var install in Session.Found)
        {
            Found.Add(new InstallChoice(install));
        }

        var environment = Session.Environment;
        var catalog = Session.Catalog;

        foreach (var note in environment?.Notes.Where(n => n.Level != NoteLevel.Info) ?? [])
        {
            Warnings.Add(note.Message);
        }

        // Without a valid signature the catalog loads nothing. Staying quiet about
        // that and leaving the user in front of an empty selection two pages later
        // would be the unfriendliest option.
        foreach (var problem in catalog is null ? [] : catalog.Errors.Concat(catalog.Warnings))
        {
            Warnings.Add(problem);
        }

        _searched = true;

        // Take over the home page selection, if there is one.
        Selected = Found.FirstOrDefault(f => f.Install == Session.Install) ?? Found.FirstOrDefault();
        Raise(nameof(NothingFound));
        NotifyChanged();
    }
}
