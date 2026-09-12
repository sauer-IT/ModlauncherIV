namespace ModlauncherIV.App;

/// <summary>Eine Seite des Assistenten.</summary>
public abstract class WizardStep(Session session) : Observable
{
    protected Session Session { get; } = session;

    private bool _isActive;

    public abstract string Title { get; }

    /// <summary>
    /// Ob der Assistent gerade auf dieser Seite steht. Die Schrittliste am Rand
    /// bindet darauf. Das hier zu speichern statt in der Liste zu vergleichen,
    /// spart einen Konverter und eine Multibinding-Konstruktion, die beide nur
    /// dasselbe herausfänden.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => Set(ref _isActive, value);
    }

    /// <summary>Ein Satz unter der Überschrift. Sagt, worum es auf dieser Seite geht.</summary>
    public abstract string Lead { get; }

    public virtual string NextLabel => "Weiter";

    public virtual bool CanGoBack => true;

    /// <summary>
    /// Ob der Assistent weiterschalten darf. Wird nach jeder Änderung neu
    /// abgefragt — die Schritte melden das über <see cref="Changed"/>.
    /// </summary>
    public virtual bool CanGoNext => true;

    /// <summary>
    /// Meldet dem Rahmen, dass sich <see cref="CanGoNext"/> geändert haben könnte.
    /// Ohne das bliebe der Weiter-Knopf grau, bis der Nutzer irgendwohin klickt.
    /// </summary>
    public event EventHandler? Changed;

    protected void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Wird beim Betreten aufgerufen, auch beim Zurückspringen.</summary>
    public virtual Task EnterAsync() => Task.CompletedTask;

    /// <summary>
    /// Wird vor dem Weiterschalten aufgerufen. False hält den Assistenten an —
    /// gedacht für Arbeit, die erst hier passieren darf.
    /// </summary>
    public virtual Task<bool> LeaveAsync() => Task.FromResult(true);
}
