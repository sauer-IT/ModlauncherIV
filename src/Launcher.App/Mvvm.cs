using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ModlauncherIV.App;

/// <summary>
/// Das Nötigste für Datenbindung. Bewusst keine MVVM-Bibliothek: der Launcher
/// schreibt in fremde Spielverzeichnisse, jede zusätzliche Abhängigkeit ist eine
/// weitere Stelle, der man dabei vertrauen müsste.
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
    /// Meldet, dass sich die Ausführbarkeit geändert hat.
    ///
    /// WPF fragt von sich aus nur bei Eingabeereignissen nach. Ein Schritt, der
    /// im Hintergrund fertig wird, löst keines aus — der Weiter-Knopf bliebe grau,
    /// bis der Nutzer irgendwohin klickt.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Ein Kommando, das auf eine asynchrone Arbeit wartet.
///
/// Der Grund, warum es das statt eines <c>async void</c>-Lambdas gibt: eine
/// Ausnahme in <c>async void</c> landet nicht beim Aufrufer, sondern auf dem
/// Thread-Pool und beendet den Prozess. Bei einem Programm, das gerade
/// hunderte Dateien im Spielverzeichnis austauscht, ist ein stiller Absturz
/// mitten in der Arbeit das Schlimmste, was passieren kann.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    /// <summary>Wird gemeldet, wenn die Arbeit mit einer Ausnahme endet.</summary>
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
            // Hier ist die Ausnahme noch auf dem UI-Thread und lässt sich zeigen.
            // Eine Zeile tiefer wäre sie es nicht mehr.
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
