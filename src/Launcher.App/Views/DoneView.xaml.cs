using System.Windows;
using System.Windows.Controls;

namespace ModlauncherIV.App;

public partial class DoneView : UserControl
{
    public DoneView() => InitializeComponent();

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (DataContext is DoneStep step)
        {
            step.Play();
        }
    }

    private void OnLock(object sender, RoutedEventArgs e)
    {
        if (DataContext is DoneStep step)
        {
            step.LockUpdates();
        }
    }
}
