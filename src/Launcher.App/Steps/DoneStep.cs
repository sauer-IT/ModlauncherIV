using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Protection;

namespace ModlauncherIV.App;

public sealed class DoneStep(Session session) : WizardStep(session)
{
    private string _headline = string.Empty;
    private bool _canLock;
    private string? _lockResult;

    public override string Title => "Fertig";

    public override string Lead => string.Empty;

    public override string NextLabel => "Zur Startseite";

    public override bool CanGoNext => true;

    public override bool CanGoBack => false;

    public string Headline
    {
        get => _headline;
        private set => Set(ref _headline, value);
    }

    public ObservableCollection<string> Installed { get; } = [];

    public ObservableCollection<string> Advice { get; } = [];

    public bool CanPlay => Session.Install is not null && File.Exists(Session.Install.ExecutablePath);

    /// <summary>True, wenn die Plattform einen Schalter hat, den wir umlegen können.</summary>
    public bool CanLockUpdates => _canLock;

    public string? LockResult
    {
        get => _lockResult;
        private set => Set(ref _lockResult, value);
    }

    /// <summary>
    /// Legt die Update-Sperre der Plattform um. Nur Steam hat einen solchen
    /// Schalter; für den Rockstar Games Launcher bleibt es beim Hinweis.
    /// </summary>
    public void LockUpdates()
    {
        var install = Session.Install;
        if (install is null)
        {
            return;
        }

        _canLock = !UpdateGuard.TryLock(install, out var message);

        LockResult = message;
        Raise(nameof(CanLockUpdates));
    }

    public override Task EnterAsync()
    {
        Installed.Clear();
        Advice.Clear();

        foreach (var name in Session.Applied)
        {
            Installed.Add(name);
        }

        Headline = Session.Applied.Count switch
        {
            0 => "Es wurde nichts verändert.",
            1 => "Ein Rezept wurde eingebaut.",
            _ => $"{Session.Applied.Count} Rezepte wurden eingebaut.",
        };

        BuildAdvice();

        Raise(nameof(CanPlay));
        Raise(nameof(CanLockUpdates));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Startet das Spiel direkt, am Plattform-Launcher vorbei.
    ///
    /// Das ist kein Komfort, sondern der Kern der Sache: Steam, Epic und der
    /// Rockstar Games Launcher prüfen beim Start die Dateien und spielen die
    /// aktuelle Version zurück. Wer nach dem Downgrade über den Launcher startet,
    /// hat das Downgrade wieder verloren.
    /// </summary>
    public void Play()
    {
        var install = Session.Install;
        if (install is null || !File.Exists(install.ExecutablePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = install.ExecutablePath,
            WorkingDirectory = install.Path,
            UseShellExecute = true,
        });
    }

    private void BuildAdvice()
    {
        var install = Session.Install;
        if (install is null)
        {
            return;
        }

        Advice.Add(
            "Starte das Spiel ab jetzt über den Knopf unten oder direkt über GTAIV.exe. "
            + "Über Steam, Epic oder den Rockstar Games Launcher gestartet, wird die "
            + "Installation geprüft und im Zweifel zurückgesetzt.");

        // Bei Steam lässt sich das automatische Update tatsächlich sperren. Das
        // anzubieten, ist sinnvoller als die Warnung allein.
        var guard = UpdateGuard.Check(install);

        _canLock = guard.State == GuardState.Unlocked;

        if (guard.State != GuardState.Locked)
        {
            Advice.Add(guard.Detail is null ? guard.Summary : $"{guard.Summary} {guard.Detail}");
        }

        if (Session.Environment?.SmartAppControl is SmartAppControlState.Enforced or SmartAppControlState.Evaluation)
        {
            Advice.Add(
                "Smart App Control ist aktiv. Es blockiert unsignierte Erweiterungen, "
                + "auch innerhalb des Spiels — Mods laden dann einfach nicht. "
                + "Abschalten geht in der Windows-Sicherheit, ist aber endgültig: "
                + "einschalten lässt es sich danach nur mit einer Neuinstallation.");
        }

        Advice.Add(
            "Zurückbauen kannst du jederzeit: der Assistent hat vor jeder Änderung "
            + "eine Sicherung angelegt.");
    }
}
