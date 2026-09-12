using System.Windows;

namespace ModlauncherIV.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _shell;

        _shell.Failed += (_, e) => MessageBox.Show(
            this,
            e.Message,
            "The step failed",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Detection may only run once the window is up — otherwise the user
        // stares at nothing for seconds.
        Loaded += async (_, _) => await _shell.StartAsync().ConfigureAwait(true);
    }
}
