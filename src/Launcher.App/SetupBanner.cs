using System.Windows;

namespace ModlauncherIV.App;

/// <summary>
/// The offer to set itself up.
///
/// Sits on the home page and on the welcome page, because those are the two
/// ways you first see the launcher. As soon as it sits in its place and appears
/// on the desktop, the offer disappears by itself.
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

    /// <summary>Whether the offer is shown at all.</summary>
    public bool Visible => !_done;

    public string Headline => "Put it on the desktop";

    public string Text =>
        "The launcher is sitting wherever you downloaded it. One click moves it "
        + "to a fixed place and creates a shortcut on the desktop and in the start "
        + "menu — then you will find it again.";

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

        // It was copied, but the old file is still running. From now on the one
        // in the fixed place should count - otherwise next time the user works on
        // a copy in the downloads folder and wonders why changes keep
        // disappearing.
        var answer = MessageBox.Show(
            $"{Result}\n\nThe launcher will now restart from its new place.",
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
