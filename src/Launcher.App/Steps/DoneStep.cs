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

    public override string Title => "Done";

    public override string Lead => string.Empty;

    public override string NextLabel => "To the home page";

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

    /// <summary>True when the platform has a switch we can flip.</summary>
    public bool CanLockUpdates => _canLock;

    public string? LockResult
    {
        get => _lockResult;
        private set => Set(ref _lockResult, value);
    }

    /// <summary>
    /// Flips the platform's update lock. Only Steam has such a switch; for the
    /// Rockstar Games Launcher it stays a note.
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
            0 => "Nothing was changed.",
            1 => "One recipe was installed.",
            _ => $"{Session.Applied.Count} recipes were installed.",
        };

        BuildAdvice();

        Raise(nameof(CanPlay));
        Raise(nameof(CanLockUpdates));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the game directly, bypassing the platform launcher.
    ///
    /// That is not convenience but the heart of the matter: Steam, Epic and the
    /// Rockstar Games Launcher check the files on start and put the current
    /// version back. Anyone starting through the launcher after a downgrade has
    /// lost that downgrade again.
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
            "From now on start the game with the button below, or directly via GTAIV.exe. "
            + "Started through Steam, Epic or the Rockstar Games Launcher, the "
            + "installation gets checked and, in case of doubt, reset.");

        // With Steam the automatic update can genuinely be locked. Offering that
        // is more useful than the warning alone.
        var guard = UpdateGuard.Check(install);

        _canLock = guard.State == GuardState.Unlocked;

        if (guard.State != GuardState.Locked)
        {
            Advice.Add(guard.Detail is null ? guard.Summary : $"{guard.Summary} {guard.Detail}");
        }

        if (Session.Environment?.SmartAppControl is SmartAppControlState.Enforced or SmartAppControlState.Evaluation)
        {
            Advice.Add(
                "Smart App Control is active. It blocks unsigned extensions, "
                + "including inside the game — mods then simply do not load. "
                + "It can be turned off in Windows Security, but that is final: "
                + "turning it back on afterwards needs a Windows reinstall.");
        }

        Advice.Add(
            "You can undo this at any time: the wizard took a backup before every "
            + "change.");
    }
}
