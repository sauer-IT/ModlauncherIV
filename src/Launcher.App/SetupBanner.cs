using System.Windows;

namespace ModlauncherIV.App;

/// <summary>
/// Das Angebot, sich einzurichten.
///
/// Sitzt auf der Startseite und auf der Willkommensseite, weil man den
/// Launcher auf beiden Wegen zum ersten Mal sieht. Sobald er an seinem Platz
/// liegt und auf dem Desktop steht, verschwindet es von selbst.
/// </summary>
public sealed class SetupBanner : Observable
{
    private string? _result;
    private bool _done;

    public SetupBanner()
    {
        SetupCommand = new RelayCommand(Run, () => !_done);
        _done = SelfInstall.IsSetUp;
    }

    public RelayCommand SetupCommand { get; }

    /// <summary>Ob das Angebot überhaupt angezeigt wird.</summary>
    public bool Visible => !_done;

    public string Headline => "Auf den Desktop legen";

    public string Text =>
        "Der Launcher liegt gerade dort, wo du ihn heruntergeladen hast. "
        + "Ein Klick legt ihn an einen festen Platz und macht eine Verknüpfung "
        + "auf dem Desktop und im Startmenü — dann findest du ihn wieder.";

    public string? Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    private void Run()
    {
        Result = SelfInstall.Run(out var relaunch);

        _done = SelfInstall.IsSetUp || !relaunch;

        Raise(nameof(Visible));
        SetupCommand.RaiseCanExecuteChanged();

        if (!relaunch)
        {
            return;
        }

        // Wurde kopiert, laeuft aber noch die alte Datei. Ab jetzt soll die am
        // festen Platz gelten - sonst bearbeitet der Nutzer beim naechsten Mal
        // eine Kopie im Downloads-Ordner und wundert sich, dass Aenderungen
        // verschwinden.
        var answer = MessageBox.Show(
            $"{Result}\n\nDer Launcher startet jetzt von seinem neuen Platz neu.",
            SelfInstall.ProgramName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        SelfInstall.RelaunchFromTarget();
        Application.Current.Shutdown();
    }
}
