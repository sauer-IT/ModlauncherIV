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
            "Der Schritt ist fehlgeschlagen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Die Erkennung darf erst laufen, wenn das Fenster steht - sonst sieht
        // der Nutzer sekundenlang nichts.
        Loaded += async (_, _) => await _shell.StartAsync().ConfigureAwait(true);
    }
}
