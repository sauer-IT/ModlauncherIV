using System.Collections.ObjectModel;
using System.Net.Http;
using ModlauncherIV.Core.Acquisition;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.App;

/// <summary>A file a recipe needs, together with its state.</summary>
public sealed class SourceRow(RecipeSource source, string recipeName) : Observable
{
    private string _state = "waiting";
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

    /// <summary>What the user has to do when the wizard cannot.</summary>
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

    public override string Title => "Getting the files";

    public override string Lead =>
        "The wizard downloads what is still missing and checks every file against "
        + "its SHA-256 checksum. If that does not match, the file is discarded.";

    public override string NextLabel => "Install";

    public ObservableCollection<SourceRow> Rows { get; } = [];

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>The folder the user has to put missing files into.</summary>
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

    /// <summary>Try again — after the user supplied a file by hand.</summary>
    public async Task RetryAsync()
    {
        _done = false;
        await EnterAsync().ConfigureAwait(true);
    }

    private async Task AcquireAsync()
    {
        Rows.Clear();
        Status = "Checking what is already there ...";
        NotifyChanged();

        var journey = Session.Journey;
        if (journey is null)
        {
            Status = "No plan available.";
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
            Status = "These recipes need no external files.";
            _done = true;
            NotifyChanged();
            return;
        }

        var acquirer = new SourceAcquirer(
            Http, Session.CacheRoot, new ExecutionLog(), AppPaths.BundledDirectory);

        foreach (var row in needed)
        {
            // Progress arrives from a background thread; Progress<T> brings it
            // back onto the one it was created on.
            var progress = new Progress<AcquisitionProgress>(p =>
            {
                row.State = "downloading ...";
                row.Percent = p.Percent;
            });

            Status = $"{row.FileName} ...";

            var result = await acquirer.AcquireAsync(row.Source, progress).ConfigureAwait(true);

            Describe(row, result);
        }

        _done = true;
        Status = Rows.Any(r => r.Failed)
            ? "Something is still missing. Without these files nothing gets installed."
            : "All present and verified.";

        Raise(nameof(NeedsUser));
        NotifyChanged();
    }

    private static void Describe(SourceRow row, AcquisitionResult result)
    {
        row.Percent = null;

        switch (result.Status)
        {
            case AcquisitionStatus.AlreadyPresent:
                row.State = "already there, checksum matches";
                break;

            case AcquisitionStatus.Downloaded:
                row.State = "downloaded and verified";
                break;

            case AcquisitionStatus.Bundled:
                row.State = "shipped, checksum matches";
                break;

            case AcquisitionStatus.NeedsUserAction:
                row.State = "has to be supplied by hand";
                row.Failed = true;

                // The catalog lists files we may not mirror for legal reasons.
                // Then the only honest answer is to say exactly which file goes
                // where — and not merely "failed".
                row.Hint = result.Source.Note
                           ?? $"Put {result.Source.FileName} into the working folder and try again.";
                break;

            default:
                row.State = "failed";
                row.Failed = true;
                row.Hint = result.Error;
                break;
        }
    }
}
