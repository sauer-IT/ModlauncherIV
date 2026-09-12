namespace ModlauncherIV.App;

/// <summary>
/// Der Rahmen: hält die Schritte, weiß, wo man gerade ist, und schaltet weiter.
/// </summary>
public sealed class WizardViewModel : Observable
{
    private readonly List<WizardStep> _steps;
    private int _index;
    private bool _busy;

    public WizardViewModel()
    {
        var session = new Session();

        _steps =
        [
            new WelcomeStep(session),
            new InstallStep(session),
            new ChoiceStep(session),
            new PlanStep(session),
            new AcquireStep(session),
            new RunStep(session),
            new DoneStep(session),
        ];

        NextCommand = new AsyncRelayCommand(NextAsync, () => !_busy && Current.CanGoNext);
        BackCommand = new AsyncRelayCommand(BackAsync, () => !_busy && _index > 0 && Current.CanGoBack);

        NextCommand.Faulted += (_, e) => Failed?.Invoke(this, e);
        BackCommand.Faulted += (_, e) => Failed?.Invoke(this, e);

        foreach (var step in _steps)
        {
            step.Changed += (_, _) => RefreshCommands();
        }
    }

    /// <summary>Eine Ausnahme, die beim Weiterschalten hochkam. Das Fenster zeigt sie.</summary>
    public event EventHandler<Exception>? Failed;

    public AsyncRelayCommand NextCommand { get; }

    public AsyncRelayCommand BackCommand { get; }

    public WizardStep Current => _steps[_index];

    public IReadOnlyList<WizardStep> Steps => _steps;

    /// <summary>"Schritt 3 von 7". Ein Assistent ohne Fortschrittsangabe fühlt sich endlos an.</summary>
    public string Position => $"Schritt {_index + 1} von {_steps.Count}";

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                RefreshCommands();
            }
        }
    }

    /// <summary>Startet den ersten Schritt. Aus dem Fenster heraus aufgerufen.</summary>
    public async Task StartAsync()
    {
        Current.IsActive = true;

        await RunGuarded(Current.EnterAsync).ConfigureAwait(true);
        RefreshCommands();
    }

    private async Task NextAsync()
    {
        if (_index >= _steps.Count - 1)
        {
            return;
        }

        // Erst fragen, ob der Schritt fertig ist. Ein Schritt, der noch arbeitet
        // oder etwas gefunden hat, darf hier anhalten.
        if (!await RunGuarded(Current.LeaveAsync).ConfigureAwait(true))
        {
            return;
        }

        await GoTo(_index + 1).ConfigureAwait(true);
    }

    private async Task BackAsync()
    {
        if (_index > 0)
        {
            await GoTo(_index - 1).ConfigureAwait(true);
        }
    }

    private async Task GoTo(int index)
    {
        _steps[_index].IsActive = false;
        _index = index;
        _steps[_index].IsActive = true;

        Raise(nameof(Current));
        Raise(nameof(Position));

        await RunGuarded(Current.EnterAsync).ConfigureAwait(true);
        RefreshCommands();
    }

    /// <summary>
    /// Führt Schrittarbeit aus und sperrt derweil die Knöpfe. Ohne die Sperre
    /// könnte der Nutzer während eines laufenden Downloads weiterklicken.
    /// </summary>
    private async Task RunGuarded(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunGuarded(Func<Task<bool>> work)
    {
        IsBusy = true;
        try
        {
            return await work().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCommands()
    {
        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
    }
}
