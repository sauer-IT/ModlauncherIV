namespace ModlauncherIV.Cli;

/// <summary>The program's exit codes. Meant for CI as well.</summary>
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
    bool Apply = false,
    bool All = false,
    string? KeyPath = null,
    string? PublicKey = null,
    string? AssumeVersion = null,
    string? TargetVersion = null,
    string? GamePath = null,
    string? CatalogPath = null,
    string? CachePath = null,
    string? OutputFile = null,
    string? SteamPath = null,
    string? EpicManifests = null,
    string? Error = null);

/// <summary>Minimal argument handling — this needs no library.</summary>
internal static class CommandLine
{
    private static readonly string[] Commands =
        ["detect", "catalog", "plan", "apply", "remove", "status", "fetch", "route", "guard", "verify",
         "journey", "catalog-key", "catalog-sign"];

    public const string HelpText = """
        mliv -- Modlauncher IV

        Usage:
          mliv <command> [argument] [options]

        Commands:
          detect                 Find installations and print a diagnostic report.
          catalog                List the available recipes.
          plan   <recipe-id>     Show what a recipe would do. Changes nothing.
          fetch  <recipe-id>     Download required files and check them by SHA-256.
          apply  <recipe-id>     Run a recipe. Asks first.
          remove <recipe-id>     Take a recipe back. --all for everything, newest first.
          status                 What the launcher changed about this installation.
          route  [version]       Which path leads to another game version.
          journey <id,id,...>    Full path to the wanted state, with dependencies.
          guard                  Whether the platform can patch the game back.
          verify                 Whether everything still sits as the launcher left it.

        Catalog maintenance tools:
          catalog-key            Create a new signing key pair.
          catalog-sign           Index and sign the catalog.  --key <file>

        Options:
          --path <folder>        Game directory, instead of searching for it.
          --steam-path <folder>  Steam's own folder, when it is not this user's.
          --epic-manifests <d>   Epic's manifest folder, likewise.
          --catalog <folder>     Recipe directory. Default: ./catalog
          --cache <folder>       Working directory for acquired files.
          --key <file>           Private key for catalog-sign.
          --public-key <base64>  A different signing key to trust.
          --allow-unsigned       Accept an unsigned catalog. Development only.
          --assume-version <v>   Give the game version when it cannot be read.
          --target <version>     For journey: the wanted game version.
          --json                 Machine-readable output (detect only).
          --out <file>           Write the output to a file.
          --yes                  Skip the confirmation in apply.
          --apply                For guard: actually set the lock.
          --all                  For remove: take every recipe back.
          -h, --help             This help.

        Exit codes:
          0  success
          1  nothing found
          2  bad usage
          3  blockers found, nothing executed
          4  output could not be written
          5  execution failed

        detect, catalog, plan and status only read. fetch writes exclusively into
        the working directory. Only apply changes the game, and even that only
        after asking and with a snapshot taken first.

        By default the catalog is only loaded with a valid signature — it decides
        which files get written into the game directory.
        """;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var index = 0;

        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            if (!Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
            {
                return options with { Error = $"Unknown command: {args[0]}" };
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

                case "--all":
                    options = options with { All = true };
                    break;

                case "--apply":
                    options = options with { Apply = true };
                    break;

                case "--allow-unsigned":
                    options = options with { AllowUnsigned = true };
                    break;

                case "--assume-version":
                    if (!TryValue(args, ref i, out var assumed))
                    {
                        return options with { Error = "--assume-version expects a version number." };
                    }

                    options = options with { AssumeVersion = assumed };
                    break;

                case "--target":
                    if (!TryValue(args, ref i, out var target))
                    {
                        return options with { Error = "--target expects a version number." };
                    }

                    options = options with { TargetVersion = target };
                    break;

                case "--public-key":
                    if (!TryValue(args, ref i, out var pub))
                    {
                        return options with { Error = "--public-key expects a base64 key." };
                    }

                    options = options with { PublicKey = pub };
                    break;

                case "--key":
                    if (!TryValue(args, ref i, out var key))
                    {
                        return options with { Error = "--key expects a file." };
                    }

                    options = options with { KeyPath = key };
                    break;

                case "--path":
                    if (!TryValue(args, ref i, out var path))
                    {
                        return options with { Error = "--path expects a folder." };
                    }

                    options = options with { GamePath = path };
                    break;

                case "--steam-path":
                    if (!TryValue(args, ref i, out var steam))
                    {
                        return options with { Error = "--steam-path expects a folder." };
                    }

                    options = options with { SteamPath = steam };
                    break;

                case "--epic-manifests":
                    if (!TryValue(args, ref i, out var epic))
                    {
                        return options with { Error = "--epic-manifests expects a folder." };
                    }

                    options = options with { EpicManifests = epic };
                    break;

                case "--catalog":
                    if (!TryValue(args, ref i, out var catalog))
                    {
                        return options with { Error = "--catalog expects a folder." };
                    }

                    options = options with { CatalogPath = catalog };
                    break;

                case "--cache":
                    if (!TryValue(args, ref i, out var cache))
                    {
                        return options with { Error = "--cache expects a folder." };
                    }

                    options = options with { CachePath = cache };
                    break;

                case "--out":
                    if (!TryValue(args, ref i, out var output))
                    {
                        return options with { Error = "--out expects a file name." };
                    }

                    options = options with { OutputFile = output };
                    break;

                default:
                    return options with { Error = $"Unknown argument: {args[i]}" };
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
