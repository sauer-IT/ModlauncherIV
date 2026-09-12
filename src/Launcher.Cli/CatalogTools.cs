using ModlauncherIV.Core.Catalog;

namespace ModlauncherIV.Cli;

/// <summary>
/// Werkzeuge für die Katalogpflege. Nicht für Endnutzer gedacht, sondern für
/// den, der den Katalog herausgibt.
/// </summary>
internal static class CatalogTools
{
    public static int CreateKey(CliOptions options)
    {
        var (privatePem, publicBase64) = CatalogSignature.CreateKey();
        var target = options.KeyPath;

        if (target is null)
        {
            Console.Error.WriteLine("Bitte mit --key angeben, wohin der private Schlüssel soll.");
            Console.Error.WriteLine("Ein Pfad AUSSERHALB des Repositorys.");
            return ExitCode.BadUsage;
        }

        var full = Path.GetFullPath(target);

        if (File.Exists(full))
        {
            // Einen bestehenden Schlüssel zu überschreiben macht jeden damit
            // signierten Katalog unbrauchbar. Das passiert nicht aus Versehen.
            Console.Error.WriteLine($"Es existiert bereits eine Datei: {full}");
            Console.Error.WriteLine("Wird nicht überschrieben — bestehende Signaturen würden ungültig.");
            return ExitCode.Failed;
        }

        var directory = Path.GetDirectoryName(full);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, privatePem);

        Console.WriteLine($"Privater Schlüssel geschrieben: {full}");
        Console.WriteLine();
        Console.WriteLine("Diesen Schlüssel NICHT ins Repository legen und nicht weitergeben.");
        Console.WriteLine("Wer ihn hat, kann Kataloge signieren, denen der Launcher vertraut.");
        Console.WriteLine();
        Console.WriteLine("Öffentlichen Teil in CatalogSignature.EmbeddedPublicKey eintragen:");
        Console.WriteLine();
        Console.WriteLine($"    public const string EmbeddedPublicKey = \"{publicBase64}\";");
        Console.WriteLine();

        return ExitCode.Ok;
    }

    public static int Sign(CliOptions options)
    {
        if (options.KeyPath is null)
        {
            Console.Error.WriteLine("Bitte mit --key den privaten Schlüssel angeben.");
            return ExitCode.BadUsage;
        }

        var directory = options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog");

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Katalogverzeichnis nicht gefunden: {directory}");
            return ExitCode.NothingFound;
        }

        if (!File.Exists(options.KeyPath))
        {
            Console.Error.WriteLine($"Schlüsseldatei nicht gefunden: {options.KeyPath}");
            return ExitCode.NothingFound;
        }

        var signed = CatalogSignature.Sign(directory, File.ReadAllText(options.KeyPath));

        Console.WriteLine($"Katalog signiert: {Path.GetFullPath(directory)}");
        Console.WriteLine($"{signed.Count} Datei(en) im Index:");

        foreach (var file in signed)
        {
            Console.WriteLine($"  {file}");
        }

        return ExitCode.Ok;
    }
}
