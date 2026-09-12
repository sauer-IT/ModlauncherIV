using System.Collections.ObjectModel;
using System.Net.Http;
using ModlauncherIV.Core.Acquisition;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.App;

/// <summary>Eine Datei, die ein Rezept braucht, mitsamt ihrem Stand.</summary>
public sealed class SourceRow(RecipeSource source, string recipeName) : Observable
{
    private string _state = "wartet";
    private int? _percent;
    private bool _failed;
    private string? _hint;

    public RecipeSource Source { get; } = source;

    public string FileName => Source.FileName;

    public string RecipeName { get; } = recipeName;

    public string Size => Source.SizeBytes >= 1024 * 1024
        ? $"{Source.SizeBytes / 1024.0 / 1024.0:0.#} MB"
        : $"{Source.SizeBytes / 1024.0:0.#} KB";

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public int? Percent
    {
        get => _percent;
        set => Set(ref _percent, value);
    }

    public bool Failed
    {
        get => _failed;
        set => Set(ref _failed, value);
    }

    /// <summary>Was der Nutzer tun muss, wenn der Assistent es nicht kann.</summary>
    public string? Hint
    {
        get => _hint;
        set => Set(ref _hint, value);
    }
}

public sealed class AcquireStep(Session session) : WizardStep(session)
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private bool _done;
    private bool _running;
    private string _status = string.Empty;

    public override string Title => "Dateien beschaffen";

    public override string Lead =>
        "Der Assistent lädt, was noch fehlt, und prüft jede Datei anhand ihrer "
        + "SHA-256-Prüfsumme. Stimmt sie nicht, wird die Datei verworfen.";

    public override string NextLabel => "Einbauen";

    public ObservableCollection<SourceRow> Rows { get; } = [];

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Der Ordner, in den der Nutzer fehlende Dateien selbst legen muss.</summary>
    public string CacheFolder => Session.CacheRoot;

    public bool NeedsUser => Rows.Any(r => r.Hint is not null);

    public override bool CanGoNext => _done && Rows.All(r => !r.Failed);

    public override async Task EnterAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            await AcquireAsync().ConfigureAwait(true);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Erneut versuchen — nachdem der Nutzer eine Datei selbst abgelegt hat.</summary>
    public async Task RetryAsync()
    {
        _done = false;
        await EnterAsync().ConfigureAwait(true);
    }

    private async Task AcquireAsync()
    {
        Rows.Clear();
        Status = "Prüfe, was schon da ist ...";
        NotifyChanged();

        var journey = Session.Journey;
        if (journey is null)
        {
            Status = "Kein Plan vorhanden.";
            return;
        }

        var needed = journey.Remaining
            .SelectMany(s => s.Recipe.RequiredFiles.Select(f => new SourceRow(f, s.Recipe.Name)))
            .ToList();

        foreach (var row in needed)
        {
            Rows.Add(row);
        }

        Raise(nameof(NeedsUser));

        if (needed.Count == 0)
        {
            Status = "Diese Rezepte brauchen keine externen Dateien.";
            _done = true;
            NotifyChanged();
            return;
        }

        var acquirer = new SourceAcquirer(Http, Session.CacheRoot, new ExecutionLog());

        foreach (var row in needed)
        {
            // Der Fortschritt kommt aus einem Hintergrund-Thread; Progress<T>
            // bringt ihn zurück auf den, auf dem es angelegt wurde.
            var progress = new Progress<AcquisitionProgress>(p =>
            {
                row.State = "lädt ...";
                row.Percent = p.Percent;
            });

            Status = $"{row.FileName} ...";

            var result = await acquirer.AcquireAsync(row.Source, progress).ConfigureAwait(true);

            Describe(row, result);
        }

        _done = true;
        Status = Rows.Any(r => r.Failed)
            ? "Es fehlt noch etwas. Ohne diese Dateien wird nichts eingebaut."
            : "Alles da und geprüft.";

        Raise(nameof(NeedsUser));
        NotifyChanged();
    }

    private static void Describe(SourceRow row, AcquisitionResult result)
    {
        row.Percent = null;

        switch (result.Status)
        {
            case AcquisitionStatus.AlreadyPresent:
                row.State = "war schon da, Prüfsumme stimmt";
                break;

            case AcquisitionStatus.Downloaded:
                row.State = "geladen und geprüft";
                break;

            case AcquisitionStatus.NeedsUserAction:
                row.State = "muss von Hand abgelegt werden";
                row.Failed = true;

                // Der Katalog nennt Dateien, die wir aus rechtlichen Gründen nicht
                // spiegeln dürfen. Dann ist die einzige ehrliche Antwort, genau zu
                // sagen, welche Datei wohin gehört — und nicht bloß "fehlgeschlagen".
                row.Hint = result.Source.Note
                           ?? $"Lege {result.Source.FileName} in den Arbeitsordner und versuche es erneut.";
                break;

            default:
                row.State = "fehlgeschlagen";
                row.Failed = true;
                row.Hint = result.Error;
                break;
        }
    }
}
