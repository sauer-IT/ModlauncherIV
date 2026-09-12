namespace ModlauncherIV.Cli;

/// <summary>Die Rückgabewerte des Programms. Auch für CI gedacht.</summary>
internal static class ExitCode
{
    public const int Ok = 0;
    public const int NothingFound = 1;
    public const int BadUsage = 2;
    public const int Blocked = 3;
    public const int OutputFailed = 4;
    public const int Failed = 5;
}

internal sealed record CliOptions(
    string Command = "detect",
    string? Argument = null,
    bool ShowHelp = false,
    bool AsJson = false,
    bool Yes = false,
    bool AllowUnsigned = false,
    string? KeyPath = null,
    string? PublicKey = null,
    string? GamePath = null,
    string? CatalogPath = null,
    string? CachePath = null,
    string? OutputFile = null,
    string? Error = null);

/// <summary>Minimale Argumentbehandlung — dafür braucht es keine Bibliothek.</summary>
internal static class CommandLine
{
    private static readonly string[] Commands =
        ["detect", "catalog", "plan", "apply", "status", "fetch", "catalog-key", "catalog-sign"];

    public const string HelpText = """
        mliv -- Modlauncher IV

        Verwendung:
          mliv <befehl> [argument] [optionen]

        Befehle:
          detect                 Installationen suchen und Diagnosebericht ausgeben.
          catalog                Verfügbare Rezepte auflisten.
          plan   <rezept-id>     Zeigen, was ein Rezept tun würde. Ändert nichts.
          fetch  <rezept-id>     Benötigte Dateien laden und per SHA-256 prüfen.
          apply  <rezept-id>     Rezept ausführen. Fragt vorher nach.
          status                 Was der Launcher an dieser Installation verändert hat.

        Werkzeuge für die Katalogpflege:
          catalog-key            Neues Signierschlüsselpaar erzeugen.
          catalog-sign           Katalog indizieren und signieren.  --key <Datei>

        Optionen:
          --path <Ordner>        Spielverzeichnis, statt danach zu suchen.
          --catalog <Ordner>     Rezeptverzeichnis. Standard: ./catalog
          --cache <Ordner>       Arbeitsverzeichnis für beschaffte Dateien.
          --key <Datei>          Privater Schlüssel für catalog-sign.
          --public-key <Base64>  Abweichender Signierschlüssel, dem vertraut wird.
          --allow-unsigned       Unsignierten Katalog zulassen. Nur zum Entwickeln.
          --json                 Maschinenlesbare Ausgabe (nur detect).
          --out <Datei>          Ausgabe in eine Datei schreiben.
          --yes                  Rückfrage bei apply überspringen.
          -h, --help             Diese Hilfe.

        Rückgabewerte:
          0  erfolgreich
          1  nichts gefunden
          2  fehlerhafter Aufruf
          3  Blocker gefunden, nichts ausgeführt
          4  Ausgabe konnte nicht geschrieben werden
          5  Ausführung fehlgeschlagen

        detect, catalog, plan und status lesen nur. fetch schreibt ausschließlich
        ins Arbeitsverzeichnis. Nur apply verändert das Spiel, und auch das nur
        nach Rückfrage und mit vorherigem Snapshot.

        Der Katalog wird standardmäßig nur mit gültiger Signatur geladen — er
        bestimmt, welche Dateien ins Spielverzeichnis geschrieben werden.
        """;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var index = 0;

        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            if (!Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
            {
                return options with { Error = $"Unbekannter Befehl: {args[0]}" };
            }

            options = options with { Command = args[0].ToLowerInvariant() };
            index = 1;

            if (args.Length > 1 && !args[1].StartsWith('-'))
            {
                options = options with { Argument = args[1] };
                index = 2;
            }
        }

        for (var i = index; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    options = options with { ShowHelp = true };
                    break;

                case "--json":
                    options = options with { AsJson = true };
                    break;

                case "--yes" or "-y":
                    options = options with { Yes = true };
                    break;

                case "--allow-unsigned":
                    options = options with { AllowUnsigned = true };
                    break;

                case "--public-key":
                    if (!TryValue(args, ref i, out var pub))
                    {
                        return options with { Error = "--public-key erwartet einen Base64-Schlüssel." };
                    }

                    options = options with { PublicKey = pub };
                    break;

                case "--key":
                    if (!TryValue(args, ref i, out var key))
                    {
                        return options with { Error = "--key erwartet eine Datei." };
                    }

                    options = options with { KeyPath = key };
                    break;

                case "--path":
                    if (!TryValue(args, ref i, out var path))
                    {
                        return options with { Error = "--path erwartet einen Ordner." };
                    }

                    options = options with { GamePath = path };
                    break;

                case "--catalog":
                    if (!TryValue(args, ref i, out var catalog))
                    {
                        return options with { Error = "--catalog erwartet einen Ordner." };
                    }

                    options = options with { CatalogPath = catalog };
                    break;

                case "--cache":
                    if (!TryValue(args, ref i, out var cache))
                    {
                        return options with { Error = "--cache erwartet einen Ordner." };
                    }

                    options = options with { CachePath = cache };
                    break;

                case "--out":
                    if (!TryValue(args, ref i, out var output))
                    {
                        return options with { Error = "--out erwartet einen Dateinamen." };
                    }

                    options = options with { OutputFile = output };
                    break;

                default:
                    return options with { Error = $"Unbekanntes Argument: {args[i]}" };
            }
        }

        return options;
    }

    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }
}
