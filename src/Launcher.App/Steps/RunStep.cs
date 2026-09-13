using System.Collections.ObjectModel;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Execution;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

public enum RunState
{
    Waiting,
    Running,
    Done,
    Failed,
}

public sealed class RunRow(string name) : Observable
{
    private RunState _state = RunState.Waiting;
    private string _detail = string.Empty;

    public string Name { get; } = name;

    public RunState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(Mark));
            }
        }
    }

    public string Mark => State switch
    {
        RunState.Running => "...",
        RunState.Done => "done",
        RunState.Failed => "failed",
        _ => "waiting",
    };

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }
}

public sealed class RunStep(Session session) : WizardStep(session)
{
    private bool _finished;
    private bool _running;
    private bool _failed;

    public override string Title => "Installing";

    public override string Lead =>
        "Before every recipe a copy of the affected files is taken. If anything "
        + "goes wrong, the previous state is restored automatically.";

    public override string NextLabel => "Finish";

    /// <summary>While writing there is no going back.</summary>
    public override bool CanGoBack => !_running && !_finished;

    public override bool CanGoNext => _finished;

    public ObservableCollection<RunRow> Rows { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public bool Failed => _failed;

    public override async Task EnterAsync()
    {
        if (_running || _finished)
        {
            return;
        }

        _running = true;
        NotifyChanged();

        try
        {
            await ApplyAllAsync().ConfigureAwait(true);
        }
        finally
        {
            _running = false;
            _finished = true;

            Raise(nameof(Failed));
            NotifyChanged();
        }
    }

    private async Task ApplyAllAsync()
    {
        var install = Session.Install;
        var journey = Session.Journey;

        if (install is null || journey is null)
        {
            return;
        }

        var rows = new Dictionary<string, RunRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in journey.Remaining)
        {
            var row = new RunRow(step.Recipe.Name);
            rows[step.Recipe.Id] = row;
            Rows.Add(row);
        }

        // The version travels along: after a downgrade the game sits on a
        // different one, and the next recipe's pre-flight has to check the new
        // one, not the one we started with.
        var version = install.Version.Raw;

        foreach (var step in journey.Remaining)
        {
            var row = rows[step.Recipe.Id];
            row.State = RunState.Running;

            var outcome = await Task.Run(() => ApplyOne(install.Path, step, version)).ConfigureAwait(true);

            foreach (var line in outcome.Log)
            {
                Log.Add(line);
            }

            if (outcome.Success)
            {
                row.State = RunState.Done;
                row.Detail = $"snapshot {outcome.SnapshotId}";

                Diary.Info($"{step.Recipe.Id} {step.Recipe.Version} installed, snapshot {outcome.SnapshotId}.");

                Session.Applied.Add(step.Recipe.Name);

                if (step.Recipe.IsVersionTransition)
                {
                    version = step.Recipe.ProducesVersion!;
                }

                continue;
            }

            row.State = RunState.Failed;
            row.Detail = string.Join(" ", outcome.Errors);

            Diary.Error($"{step.Recipe.Id} failed: {string.Join(" ", outcome.Errors)}");
            Diary.Info(outcome.RolledBack
                ? "The previous state was restored from the snapshot."
                : "Nothing had been changed yet.");

            foreach (var error in outcome.Errors)
            {
                Log.Add(error);
            }

            Log.Add(outcome.RolledBack
                ? "The previous state was restored."
                : "Nothing was changed.");

            // Do not carry on after a failure: the recipes that follow build on
            // what just did not happen.
            _failed = true;

            foreach (var pending in Rows.Where(r => r.State == RunState.Waiting))
            {
                pending.Detail = "skipped";
            }

            return;
        }
    }

    /// <summary>One recipe, through the whole transaction. The home page's Update uses it too.</summary>
    internal static ExecutionOutcome ApplyOne(string gameRoot, JourneyStep step, string version)
    {
        var context = new RecipeContext(
            gameRoot: gameRoot,
            sourceRoot: AppPaths.Cache,
            log: new ExecutionLog(Diary.Line),
            dryRun: false);

        var runner = new TransactionRunner(new SnapshotStore(gameRoot), new LedgerStore(gameRoot));
        var plan = runner.Plan(step.Recipe, context, version);

        if (!plan.CanRun)
        {
            return new ExecutionOutcome(
                Success: false,
                SnapshotId: null,
                RolledBack: false,
                Errors: plan.Issues
                    .Where(i => i.Severity == IssueSeverity.Fatal)
                    .Select(i => i.Detail is null ? i.Message : $"{i.Message} {i.Detail}")
                    .ToArray(),
                Log: []);
        }

        return runner.Apply(plan, context);
    }
}
