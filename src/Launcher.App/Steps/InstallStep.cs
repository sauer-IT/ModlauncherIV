using System.Collections.ObjectModel;
using System.IO;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>Eine gefundene Installation, wie sie in der Liste steht.</summary>
public sealed class InstallChoice(GameInstall install)
{
    public GameInstall Install { get; } = install;

    public string Path => Install.Path;

    public string Platform => Install.Platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Datenträger",
        _ => "unbekannte Herkunft",
    };

    public string Version => Install.Version.IsKnown
        ? $"{Install.Version.Raw} — {Install.Version.DisplayName}"
        : $"{Install.Version.Raw} (nicht zugeordnet)";

    public string Extras => Install switch
    {
        { HasTlad: true, HasTbogt: true } => "mit beiden Episoden",
        { HasTlad: true } => "mit The Lost and Damned",
        { HasTbogt: true } => "mit The Ballad of Gay Tony",
        _ => "ohne Episoden",
    };

    public string Mods => Install.ModArtifacts.Count == 0
        ? "unverändert"
        : $"{Install.ModArtifacts.Count} Fremddatei(en) gefunden";
}

public sealed class InstallStep(Session session) : WizardStep(session)
{
    private InstallChoice? _selected;
    private string? _error;
    private bool _searched;

    public override string Title => "Installation";

    public override string Lead =>
        "Der Assistent sucht GTA IV in der Registry, bei Steam und Epic sowie an "
        + "den üblichen Orten. Ist deine Installation nicht dabei, gib den Ordner an.";

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
    /// Nimmt einen von Hand gewählten Ordner auf. Wird vom Fenster aufgerufen,
    /// nachdem der Nutzer im Dateidialog bestätigt hat.
    /// </summary>
    public void AddManually(string path)
    {
        Error = null;

        if (!File.Exists(Path.Combine(path, InstallInspector.ExecutableName)))
        {
            Error = $"In {path} liegt keine {InstallInspector.ExecutableName}.";
            return;
        }

        if (Found.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Selected = Found.First(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var locator = new InstallLocator();
        var candidate = new InstallCandidate(path, locator.InferPlatform(path), "von Hand angegeben");
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

        // Die Hülle hat beim Start schon gesucht. Hier noch einmal zu suchen
        // wäre nicht nur langsam, sondern könnte auch ein anderes Ergebnis
        // liefern als das, was die Startseite gerade angezeigt hat.
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

        // Ohne gültige Signatur lädt der Katalog nichts. Das hier zu verschweigen
        // und den Nutzer zwei Seiten später vor einer leeren Auswahl stehen zu
        // lassen, wäre die unfreundlichste Variante.
        foreach (var problem in catalog is null ? [] : catalog.Errors.Concat(catalog.Warnings))
        {
            Warnings.Add(problem);
        }

        _searched = true;

        // Die Auswahl der Startseite übernehmen, falls es eine gibt.
        Selected = Found.FirstOrDefault(f => f.Install == Session.Install) ?? Found.FirstOrDefault();
        Raise(nameof(NothingFound));
        NotifyChanged();
    }
}
