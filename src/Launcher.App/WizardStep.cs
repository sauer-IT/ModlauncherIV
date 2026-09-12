namespace ModlauncherIV.App;

/// <summary>One page of the wizard.</summary>
public abstract class WizardStep(Session session) : Observable
{
    protected Session Session { get; } = session;

    private bool _isActive;

    public abstract string Title { get; }

    /// <summary>
    /// Whether the wizard is currently on this page. The step list on the side
    /// binds to it. Storing it here rather than comparing inside the list saves
    /// a converter and a multi-binding construction that would both only work
    /// out the same thing.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => Set(ref _isActive, value);
    }

    /// <summary>One sentence under the heading. Says what this page is about.</summary>
    public abstract string Lead { get; }

    public virtual string NextLabel => "Next";

    public virtual bool CanGoBack => true;

    /// <summary>
    /// Whether the wizard may move on. Asked again after every change — the
    /// steps signal that via <see cref="Changed"/>.
    /// </summary>
    public virtual bool CanGoNext => true;

    /// <summary>
    /// Tells the shell that <see cref="CanGoNext"/> may have changed. Without
    /// this the Next button would stay grey until the user clicks somewhere.
    /// </summary>
    public event EventHandler? Changed;

    protected void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Called on entering, including when going back.</summary>
    public virtual Task EnterAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before moving on. False stops the wizard — meant for work that may
    /// only happen at this point.
    /// </summary>
    public virtual Task<bool> LeaveAsync() => Task.FromResult(true);
}
