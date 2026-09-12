namespace ModlauncherIV.App;

/// <summary>
/// Ein Hinweis auf der Startseite.
///
/// Eine eigene Klasse und kein Tupel: WPF bindet an Eigenschaften, und die
/// Bestandteile eines ValueTuple sind Felder. Eine Bindung darauf schlaegt nicht
/// fehl, sie bleibt einfach leer - der unangenehmste Fehler, den es in XAML gibt.
/// </summary>
public sealed record Point(string Head, string Text);

public sealed class WelcomeStep(Session session) : WizardStep(session)
{
    public override string Title => "Willkommen";

    public override string Lead =>
        "Dieser Assistent bringt GTA IV auf eine Version, die sich modden lässt, "
        + "und baut anschließend ein, was du auswählst.";

    public override string NextLabel => "Los geht's";

    public override bool CanGoBack => false;

    /// <summary>
    /// Was der Nutzer wissen muss, bevor irgendetwas passiert.
    ///
    /// Diese Punkte stehen am Anfang und nicht im Kleingedruckten, weil jeder
    /// einzelne davon nachher zu einem "der Launcher hat mein Spiel zerstört"
    /// führen kann — und weil sie sich hinterher nicht mehr erklären lassen.
    /// </summary>
    public IReadOnlyList<Point> Points =>
    [
        new("Dein Spielstand bleibt",
         "Verändert wird nur die Installation, nicht der Ordner mit den Spielständen "
         + "unter Dokumente. Trotzdem gilt: eine Sicherung hat noch nie geschadet."),

        new("Alles ist umkehrbar",
         "Vor jeder Änderung wird eine Kopie der betroffenen Dateien angelegt. "
         + "Schlägt etwas fehl, wird der vorherige Zustand automatisch "
         + "wiederhergestellt. Einzeln zurückbauen geht später jederzeit."),

        new("Nicht mehr über den Launcher starten",
         "Rockstar Games Launcher, Steam und Epic prüfen beim Start, ob die Dateien "
         + "noch die erwarteten sind — und spielen die neue Version zurück. Nach dem "
         + "Downgrade startest du das Spiel über diesen Assistenten."),

        new("Es werden Dateien aus dem Netz geladen",
         "Der Assistent lädt nur von den Adressen, die im Katalog stehen, und prüft "
         + "jede Datei anhand ihrer SHA-256-Prüfsumme. Stimmt die nicht, wird die "
         + "Datei verworfen und nichts installiert."),
    ];
}
