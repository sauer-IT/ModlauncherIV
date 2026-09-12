namespace ModlauncherIV.App;

/// <summary>
/// Die Hülle: entscheidet beim Start, ob die Startseite oder der Assistent
/// gezeigt wird, und schaltet zwischen beiden um.
///
/// Beide teilen sich dieselbe <see cref="Session"/>. Der Assistent findet
/// deshalb die Installation schon vor, die die Startseite angezeigt hat - und
/// die Startseite sieht nach einem Durchlauf sofort, was dazugekommen ist.
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

    private void ShowWizard()
    {
        var wizard = new WizardViewModel(_session);

        wizard.Failed += (_, e) => Failed?.Invoke(this, e);
        wizard.Finished += async (_, _) => await ShowHomeAsync().ConfigureAwait(true);

        Current = wizard;

        // Der erste Schritt darf erst laufen, wenn die Ansicht steht.
        _ = wizard.StartAsync();
    }

    private async Task ShowHomeAsync()
    {
        var home = new HomeViewModel(_session, ShowWizard);

        Current = home;
        await home.EnterAsync().ConfigureAwait(true);
    }
}
