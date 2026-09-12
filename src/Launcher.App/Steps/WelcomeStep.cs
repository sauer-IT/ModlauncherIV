namespace ModlauncherIV.App;

/// <summary>
/// One note on the welcome page.
///
/// Its own class rather than a tuple: WPF binds to properties, and the parts of
/// a ValueTuple are fields. A binding to those does not fail, it simply stays
/// empty — the nastiest kind of bug XAML has to offer.
/// </summary>
public sealed record Point(string Head, string Text);

public sealed class WelcomeStep(Session session) : WizardStep(session)
{
    public override string Title => "Welcome";

    public override string Lead =>
        "This wizard brings GTA IV to a version that can be modded, and then "
        + "installs whatever you pick.";

    public override string NextLabel => "Let's go";

    public override bool CanGoBack => false;

    /// <summary>The offer to put itself on the desktop.</summary>
    public SetupBanner Setup { get; } = new();

    /// <summary>
    /// What the user needs to know before anything happens.
    ///
    /// These points sit at the beginning and not in the small print, because
    /// every single one of them can otherwise turn into "the launcher wrecked my
    /// game" — and because afterwards they can no longer be explained.
    /// </summary>
    public IReadOnlyList<Point> Points =>
    [
        new("Your save games stay",
         "Only the installation is changed, not the save game folder under "
         + "Documents. Still: a backup has never hurt anyone."),

        new("Everything is reversible",
         "Before every change, a copy of the affected files is taken. If anything "
         + "fails, the previous state is restored automatically. Removing things "
         + "one at a time is possible at any point later."),

        new("Stop starting it through the launcher",
         "Rockstar Games Launcher, Steam and Epic check on start whether the files "
         + "are the expected ones — and put the new version back. After the "
         + "downgrade you start the game through this wizard."),

        new("Files will be downloaded",
         "The wizard only downloads from the addresses listed in the catalog, and "
         + "checks every file against its SHA-256 checksum. If that does not "
         + "match, the file is discarded and nothing is installed."),
    ];
}
