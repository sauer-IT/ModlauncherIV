using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>
/// Die Erkennung, die beim Start einmal läuft.
///
/// Sie liegt hier und nicht im ersten Assistentenschritt, weil inzwischen zwei
/// Seiten von ihr abhängen: die Startseite muss wissen, ob überhaupt schon etwas
/// eingerichtet ist, bevor sie sich zeigt. Zweimal zu suchen wäre nicht nur
/// langsam, sondern könnte auch zwei Ergebnisse liefern.
/// </summary>
public static class Detection
{
    public static Task FillAsync(Session session) => Task.Run(() =>
    {
        var found = new InstallLocator().Locate()
            .Select(c => new InstallInspector().Inspect(c))
            .ToList();

        session.Found = found;
        session.Environment = SystemEnvironmentProbe.Probe();
        session.Catalog = RecipeCatalog.LoadFrom(AppPaths.CatalogDirectory, CatalogTrust.RequireSignature);

        // Nur eine gefunden? Dann ist sie gemeint. Bei mehreren entscheidet der
        // Nutzer, und bis dahin bleibt nichts ausgewählt.
        session.Install = found.Count == 1 ? found[0] : null;
    });
}
