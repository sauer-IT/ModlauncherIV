using System.Windows;

namespace ModlauncherIV.App;

public partial class MainWindow : Window
{
    private readonly WizardViewModel _wizard = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _wizard;

        _wizard.Failed += (_, e) => MessageBox.Show(
            this,
            e.Message,
            "Der Schritt ist fehlgeschlagen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Der erste Schritt sucht die Installation - das darf erst laufen, wenn
        // das Fenster steht, sonst sieht der Nutzer sekundenlang nichts.
        Loaded += async (_, _) => await _wizard.StartAsync().ConfigureAwait(true);
    }
}
