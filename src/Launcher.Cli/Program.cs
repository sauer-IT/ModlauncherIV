using System.Text;
using ModlauncherIV.Core.Catalog;
using Detection = ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = CommandLine.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(CommandLine.HelpText);
            return ExitCode.Ok;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine($"Fehler: {options.Error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.HelpText);
            return ExitCode.BadUsage;
        }

        try
        {
            return options.Command switch
            {
                "detect" => DetectCommand.Run(options),
                "catalog" => RecipeCommands.ShowCatalog(options),
                "plan" => RecipeCommands.Plan(options),
                "apply" => RecipeCommands.Apply(options),
                "status" => RecipeCommands.Status(options),
                "journey" => JourneyCommand.Run(options),
                "fetch" => await Fetch(options).ConfigureAwait(false),
                "route" => WithInstall(options, i => StateCommands.Route(
                    options, i, RecipeCommands.LoadCatalog(options))),
                "guard" => WithInstall(options, i => StateCommands.Guard(options, i)),
                "verify" => WithInstall(options, StateCommands.Verify),
                "remove" => WithInstall(options, i => RemoveCommand.Run(
                    options, i, RecipeCommands.LoadCatalog(options))),
                "catalog-key" => CatalogTools.CreateKey(options),
                "catalog-sign" => CatalogTools.Sign(options),
                _ => Unknown(options.Command),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Ein Diagnosewerkzeug darf nie mit einem Stacktrace enden.
            Console.Error.WriteLine($"Abgebrochen: {e.Message}");
            return ExitCode.Failed;
        }
    }

    /// <summary>Führt einen Befehl aus, der eine gefundene Installation braucht.</summary>
    private static int WithInstall(CliOptions options, Func<Detection.GameInstall, int> command)
    {
        var install = RecipeCommands.FindInstall(options);
        return install is null ? ExitCode.NothingFound : command(install);
    }

    private static async Task<int> Fetch(CliOptions options)
    {
        var catalog = RecipeCatalog.LoadFrom(
            options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog"),
            options.AllowUnsigned ? CatalogTrust.AllowUnsigned : CatalogTrust.RequireSignature,
            options.PublicKey);

        foreach (var warning in catalog.Warnings)
        {
            Console.Error.WriteLine($"Achtung: {warning}");
        }

        foreach (var error in catalog.Errors)
        {
            Console.Error.WriteLine($"Katalogfehler: {error}");
        }

        return await FetchCommand.RunAsync(options, catalog).ConfigureAwait(false);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unbekannter Befehl: {command}");
        return ExitCode.BadUsage;
    }
}
