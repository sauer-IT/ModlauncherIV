using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Planning;
using ModlauncherIV.Core.Verification;

namespace ModlauncherIV.App;

/// <summary>Ein installiertes Rezept, wie es auf der Startseite steht.</summary>
public sealed record InstalledMod(string Name, string Version, string State, bool Intact);

/// <summary>
/// Die Startseite.
///
/// Wer sein Spiel schon eingerichtet hat, will spielen — nicht sieben Schritte
/// durchklicken, um am Ende zu erfahren, dass es nichts zu tun gibt. Der
/// Assistent bleibt genau einen Klick entfernt.
/// </summary>
public sealed class HomeViewModel : Observable
{
    private readonly Session _session;
    private string _status = string.Empty;
    private bool _healthy = true;
    private bool _busy;

    public HomeViewModel(Session session, Action openWizard)
    {
        _session = session;

        PlayCommand = new RelayCommand(Play, () => CanPlay);
        WizardCommand = new RelayCommand(openWizard);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !_busy);
        FolderCommand = new RelayCommand(OpenFolder, () => _session.Install is not null);
    }

    public RelayCommand PlayCommand { get; }

    public RelayCommand WizardCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public RelayCommand FolderCommand { get; }

    public ObservableCollection<InstalledMod> Mods { get; } = [];

    /// <summary>Das Angebot, sich auf den Desktop zu legen. Verschwindet, sobald erledigt.</summary>
    public SetupBanner Setup { get; } = new();

    public string GamePath => _session.Install?.Path ?? "keine Installation";

    public string Version => _session.Install is { } install
        ? $"{install.Version.Raw} — {install.Version.DisplayName}"
        : string.Empty;

    public string Platform => _session.Install?.Platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Datenträger",
        _ => "unbekannte Herkunft",
    };

    /// <summary>Der Satz über dem Spiel-starten-Knopf. Sagt, ob etwas nicht stimmt.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>False, wenn die Gegenprobe etwas gefunden hat. Färbt den Status.</summary>
    public bool Healthy
    {
        get => _healthy;
        private set => Set(ref _healthy, value);
    }

    public bool CanPlay => _session.Install is not null && File.Exists(_session.Install.ExecutablePath);

    /// <summary>Erste Anzeige. Die Gegenprobe läuft dabei gleich mit.</summary>
    public Task EnterAsync() => VerifyAsync();

    /// <summary>
    /// Startet das Spiel direkt, am Plattform-Launcher vorbei.
    ///
    /// Das ist kein Komfort, sondern der Kern der Sache: Steam, Epic und der
    /// Rockstar Games Launcher prüfen beim Start die Dateien und spielen die
    /// aktuelle Version zurück. Wer nach dem Downgrade über den Launcher startet,
    /// hat das Downgrade wieder verloren.
    /// </summary>
    private void Play()
    {
        if (_session.Install is not { } install || !File.Exists(install.ExecutablePath))
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

    private void OpenFolder()
    {
        if (_session.Install is { } install && Directory.Exists(install.Path))
        {
            Process.Start(new ProcessStartInfo { FileName = install.Path, UseShellExecute = true });
        }
    }

    private async Task VerifyAsync()
    {
        if (_session.Install is not { } install)
        {
            Status = "Keine Installation gefunden.";
            Healthy = false;
            return;
        }

        _busy = true;
        VerifyCommand.RaiseCanExecuteChanged();
        Status = "Prüfe die Installation ...";

        try
        {
            var ledger = _session.Ledger;

            // Prüfsummen über hunderte Dateien — das gehört nicht auf den Thread,
            // der das Fenster zeichnet.
            var result = await Task.Run(() => InstallVerifier.Verify(install, ledger)).ConfigureAwait(true);

            Mods.Clear();

            foreach (var entry in ledger.Entries.OrderBy(e => e.InstalledAt))
            {
                var broken = result.Files.Count(f =>
                    string.Equals(f.RecipeId, entry.RecipeId, StringComparison.OrdinalIgnoreCase) &&
                    f.State is OwnedFileState.Modified or OwnedFileState.Missing);

                Mods.Add(new InstalledMod(
                    entry.RecipeName,
                    entry.RecipeVersion,
                    broken == 0 ? $"{entry.Files.Count} Datei(en)" : $"{broken} Datei(en) verändert oder weg",
                    broken == 0));
            }

            Describe(result, ledger);
        }
        finally
        {
            _busy = false;
            VerifyCommand.RaiseCanExecuteChanged();
        }
    }

    private void Describe(VerificationResult result, InstallLedger ledger)
    {
        if (ledger.Entries.Count == 0)
        {
            Status = "An dieser Installation hat der Launcher noch nichts verändert.";
            Healthy = true;
            return;
        }

        // Die Version zuerst: wurde zurückgepatcht, sind alle anderen Befunde
        // nur Folgen davon, und die Ursache steht sonst unten in einer Liste.
        if (result.VersionReverted)
        {
            Status = $"Die Plattform hat das Spiel auf {result.CurrentVersion} zurückgesetzt. "
                   + $"Eingebaut war {result.ExpectedVersion}. Die Mods laden so nicht.";

            Healthy = false;
            return;
        }

        if (!result.IsIntact)
        {
            Status = $"{result.ModifiedCount + result.MissingCount} Datei(en) sind nicht mehr so, "
                   + "wie der Launcher sie hinterlassen hat.";

            Healthy = false;
            return;
        }

        Status = "Alles bereit.";
        Healthy = true;
    }

    /// <summary>
    /// Ob diese Installation eingerichtet genug ist, um die Startseite zu zeigen.
    ///
    /// Die Schwelle ist bewusst niedrig: schon ein einziges eingebautes Rezept
    /// heisst, dass hier jemand war und weiss, was er tut. Wer dagegen ein
    /// unberuehrtes Spiel hat, soll den Assistenten sehen und nicht einen
    /// Spiel-starten-Knopf, der ihm nichts bringt.
    /// </summary>
    public static bool LooksConfigured(Session session) =>
        session.Install is not null && session.Ledger.Entries.Count > 0;
}
