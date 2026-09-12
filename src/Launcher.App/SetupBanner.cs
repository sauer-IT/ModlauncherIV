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
    private SetupState _state;

    public SetupBanner()
    {
        SetupCommand = new RelayCommand(Run, () => _state != SetupState.Done);
        _state = SelfInstall.State;
    }

    public RelayCommand SetupCommand { get; }

    /// <summary>Whether the offer is shown at all.</summary>
    public bool Visible => _state != SetupState.Done;

    public string Headline => _state == SetupState.Outdated
        ? "The copy on your desktop is out of date"
        : "Put it on the desktop";

    public string Text => _state == SetupState.Outdated
        ? "The version you are running is newer than the one the desktop "
          + "shortcut points at. One click replaces it — otherwise you keep "
          + "starting the old one."
        : "The launcher is sitting wherever you downloaded it. One click moves it "
          + "to a fixed place and creates a shortcut on the desktop and in the start "
          + "menu — then you will find it again.";

    public string Action => _state == SetupState.Outdated ? "Update" : "Set up";

    public string? Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    private void Run()
    {
        Result = SelfInstall.Run(out var relaunch);

        _state = relaunch ? SetupState.Done : SelfInstall.State;

        Raise(nameof(Visible));
        Raise(nameof(Headline));
        Raise(nameof(Text));
        Raise(nameof(Action));
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
