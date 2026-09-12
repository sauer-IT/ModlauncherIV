using System.Windows;

namespace ModlauncherIV.App;

public partial class OnlineWindow : Window
{
    public OnlineWindow(OnlineViewModel model)
    {
        InitializeComponent();

        DataContext = model;

        // The list is asked for once the window is on screen, not before: the
        // master list is somebody else's server and may take seconds, and a
        // button that appears to do nothing for that long is worse than a
        // window that says what it is waiting for.
        Loaded += async (_, _) => await model.LoadAsync().ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
