using System.Text;

namespace ModlauncherIV.Cli;

internal static class Program
{
    private static int Main(string[] args)
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

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unbekannter Befehl: {command}");
        return ExitCode.BadUsage;
    }
}
