namespace ModlauncherIV.App;

/// <summary>
/// The shell: holds the steps, knows where you are, and moves on.
/// </summary>
public sealed class WizardViewModel : Observable
{
    private readonly List<WizardStep> _steps;
    private int _index;
    private bool _busy;

    /// <param name="canGoHome">
    /// Whether there is a home page to go back to.
    ///
    /// Only when the wizard was opened from it. On a first run nothing is
    /// configured yet and the home page has nothing to show - stranding somebody
    /// there would be worse than the step they were on, so the way out is simply
    /// not offered.
    /// </param>
    public WizardViewModel(Session session, bool canGoHome = false)
    {
        CanGoHome = canGoHome;

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

        // Leaving is barred while something is running, and only then. A recipe
        // is mid-transaction at that point; walking away from it would be the
        // one thing the whole pipeline exists to prevent.
        HomeCommand = new RelayCommand(Finish, () => CanGoHome && !_busy);

        NextCommand.Faulted += (_, e) => Failed?.Invoke(this, e);
        BackCommand.Faulted += (_, e) => Failed?.Invoke(this, e);

        foreach (var step in _steps)
        {
            step.Changed += (_, _) => RefreshCommands();
        }
    }

    /// <summary>An exception raised while moving on. The window shows it.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>The user reached the end and wants to go back to the home page.</summary>
    public event EventHandler? Finished;

    public void Finish() => Finished?.Invoke(this, EventArgs.Empty);

    public AsyncRelayCommand NextCommand { get; }

    public AsyncRelayCommand BackCommand { get; }

    /// <summary>Out of the wizard and back to the home page.</summary>
    public RelayCommand HomeCommand { get; }

    /// <summary>Whether that way out exists at all. See the constructor.</summary>
    public bool CanGoHome { get; }

    /// <summary>
    /// Whether the way out needs its own button.
    ///
    /// Not on the last step: "Next" already leads home there, and two buttons
    /// side by side doing the same thing make the reader look for the
    /// difference between them.
    /// </summary>
    public bool ShowHomeButton => CanGoHome && _index < _steps.Count - 1;

    public WizardStep Current => _steps[_index];

    public IReadOnlyList<WizardStep> Steps => _steps;

    /// <summary>"Step 3 of 7". A wizard without progress feels endless.</summary>
    public string Position => $"Step {_index + 1} of {_steps.Count}";

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

    /// <summary>Starts the first step. Called from the window.</summary>
    public async Task StartAsync()
    {
        Current.IsActive = true;

        await RunGuarded(Current.EnterAsync).ConfigureAwait(true);
        RefreshCommands();
    }

    private async Task NextAsync()
    {
        // On the last page "Next" leads out of the wizard instead of doing
        // nothing. A grey button at the end leaves the user at a loss right
        // when they have just finished.
        if (_index >= _steps.Count - 1)
        {
            Finish();
            return;
        }

        // Ask first whether the step is done. A step that is still working or
        // has found something may stop here.
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
        Raise(nameof(ShowHomeButton));

        await RunGuarded(Current.EnterAsync).ConfigureAwait(true);
        RefreshCommands();
    }

    /// <summary>
    /// Runs step work and locks the buttons meanwhile. Without that lock the
    /// user could click on during a running download.
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
        HomeCommand.RaiseCanExecuteChanged();
    }
}
