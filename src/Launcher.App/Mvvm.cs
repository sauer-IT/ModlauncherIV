using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ModlauncherIV.App;

/// <summary>
/// The bare minimum for data binding. Deliberately no MVVM library: the launcher
/// writes into other people's game directories, and every extra dependency is one
/// more thing you would have to trust while doing that.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    /// <summary>
    /// Signals that whether this can run may have changed.
    ///
    /// WPF only asks again on input events by itself. A step finishing in the
    /// background raises none of those — the Next button would stay grey until
    /// the user clicks somewhere.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A command that awaits asynchronous work.
///
/// The reason this exists instead of an <c>async void</c> lambda: an exception
/// inside <c>async void</c> does not reach the caller but lands on the thread
/// pool and kills the process. For a program that is in the middle of swapping
/// hundreds of files in the game directory, a silent crash mid-work is the
/// worst thing that can happen.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    /// <summary>Raised when the work ends in an exception.</summary>
    public event EventHandler<Exception>? Faulted;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            // Here the exception is still on the UI thread and can be shown.
            // One line further down it would not be.
            Faulted?.Invoke(this, e);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
