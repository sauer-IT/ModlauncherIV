namespace ModlauncherIV.App;

/// <summary>
/// The shell: decides at startup whether the home page or the wizard is shown,
/// and switches between the two.
///
/// Both share the same <see cref="Session"/>. The wizard therefore already
/// finds the installation the home page displayed — and the home page sees
/// immediately after a run what has been added.
/// </summary>
public sealed class ShellViewModel : Observable
{
    private readonly Session _session = new();
    private object? _current;

    public object? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public event EventHandler<Exception>? Failed;

    public async Task StartAsync()
    {
        await Detection.FillAsync(_session).ConfigureAwait(true);

        if (HomeViewModel.LooksConfigured(_session))
        {
            await ShowHomeAsync().ConfigureAwait(true);
            return;
        }

        ShowWizard();
    }

    /// <param name="fromHome">
    /// Whether the home page is behind us. Only then does the wizard offer a way
    /// back to it - at startup, with nothing configured yet, there is no home
    /// page worth returning to.
    /// </param>
    private void ShowWizard(bool fromHome = false)
    {
        var wizard = new WizardViewModel(_session, fromHome);

        wizard.Failed += (_, e) => Failed?.Invoke(this, e);
        wizard.Finished += async (_, _) => await ShowHomeAsync().ConfigureAwait(true);

        Current = wizard;

        // The first step may only run once the view is up.
        _ = wizard.StartAsync();
    }

    private async Task ShowHomeAsync()
    {
        var home = new HomeViewModel(_session, () => ShowWizard(fromHome: true));

        Current = home;
        await home.EnterAsync().ConfigureAwait(true);
    }
}
