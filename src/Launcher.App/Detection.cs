using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.App;

/// <summary>
/// The detection that runs once at startup.
///
/// It lives here and not in the first wizard step because two pages depend on
/// it now: the home page has to know whether anything is set up at all before
/// it shows itself. Searching twice would not only be slow, it could also
/// produce two different answers.
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

        // Only one found? Then that is the one. With several the user decides,
        // and until then nothing is selected.
        session.Install = found.Count == 1 ? found[0] : null;
    });
}
