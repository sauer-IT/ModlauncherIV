using ModlauncherIV.Core.Catalog;

namespace ModlauncherIV.Cli;

/// <summary>
/// Tools for maintaining the catalog. Not meant for end users but for whoever
/// publishes the catalog.
/// </summary>
internal static class CatalogTools
{
    public static int CreateKey(CliOptions options)
    {
        var (privatePem, publicBase64) = CatalogSignature.CreateKey();
        var target = options.KeyPath;

        if (target is null)
        {
            Console.Error.WriteLine("Use --key to say where the private key should go.");
            Console.Error.WriteLine("A path OUTSIDE the repository.");
            return ExitCode.BadUsage;
        }

        var full = Path.GetFullPath(target);

        if (File.Exists(full))
        {
            // Overwriting an existing key makes every catalog signed with it
            // useless. That does not happen by accident.
            Console.Error.WriteLine($"A file already exists there: {full}");
            Console.Error.WriteLine("Not overwritten — existing signatures would become invalid.");
            return ExitCode.Failed;
        }

        var directory = Path.GetDirectoryName(full);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, privatePem);

        Console.WriteLine($"Private key written: {full}");
        Console.WriteLine();
        Console.WriteLine("Do NOT put this key in the repository and do not pass it on.");
        Console.WriteLine("Whoever has it can sign catalogs that the launcher trusts.");
        Console.WriteLine();
        Console.WriteLine("Put the public part into CatalogSignature.EmbeddedPublicKey:");
        Console.WriteLine();
        Console.WriteLine($"    public const string EmbeddedPublicKey = \"{publicBase64}\";");
        Console.WriteLine();

        return ExitCode.Ok;
    }

    public static int Sign(CliOptions options)
    {
        if (options.KeyPath is null)
        {
            Console.Error.WriteLine("Use --key to give the private key.");
            return ExitCode.BadUsage;
        }

        var directory = options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog");

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Catalog directory not found: {directory}");
            return ExitCode.NothingFound;
        }

        if (!File.Exists(options.KeyPath))
        {
            Console.Error.WriteLine($"Key file not found: {options.KeyPath}");
            return ExitCode.NothingFound;
        }

        var signed = CatalogSignature.Sign(directory, File.ReadAllText(options.KeyPath));

        Console.WriteLine($"Catalog signed: {Path.GetFullPath(directory)}");
        Console.WriteLine($"{signed.Count} file(s) in the index:");

        foreach (var file in signed)
        {
            Console.WriteLine($"  {file}");
        }

        return ExitCode.Ok;
    }
}
