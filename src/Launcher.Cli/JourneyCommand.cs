using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.Cli;

/// <summary>
/// Zeigt den Weg von der vorgefundenen Installation zu einem Wunschzustand.
///
/// Das ist derselbe Plan, den später der Assistent abarbeitet. Er hängt hier an
/// der CLI, weil ein Fenster sich nicht gegen Fixtures testen lässt und die
/// Reihenfolgelogik zu wichtig ist, um sie nur von Hand zu prüfen.
/// </summary>
internal static class JourneyCommand
{
    public static int Run(CliOptions options)
    {
        var install = RecipeCommands.FindInstall(options);
        if (install is null)
        {
            return ExitCode.NothingFound;
        }

        var catalog = RecipeCommands.LoadCatalog(options);

        // Mehrere Rezepte durch Komma getrennt. Leere Liste ist erlaubt: dann
        // fragt man nur nach dem Versionswechsel.
        var wanted = (options.Argument ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var journey = JourneyPlanner.Plan(
            new JourneyRequest(options.TargetVersion, wanted),
            catalog.Recipes,
            install.Version.Raw,
            new LedgerStore(install.Path).Load());

        Print(journey, install);

        // Ein Weg mit offenen Schritten ist kein Fehler, sondern der Normalfall —
        // der Befehl plant, er führt nicht aus.
        return journey.IsPossible ? ExitCode.Ok : ExitCode.Blocked;
    }

    private static void Print(Journey journey, GameInstall install)
    {
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("  Weg zum Wunschzustand");
        Console.WriteLine(new string('=', 74));
        Console.WriteLine();
        Console.WriteLine($"  Spiel          {install.Path}");
        Console.WriteLine($"  Jetzt          {journey.FromVersion}");
        Console.WriteLine($"  Ziel           {journey.TargetVersion}");
        Console.WriteLine();

        if (journey.Steps.Count == 0)
        {
            Console.WriteLine("  Es ist nichts zu tun.");
        }
        else
        {
            Console.WriteLine("  SCHRITTE");
            Console.WriteLine("  --------------");

            for (var i = 0; i < journey.Steps.Count; i++)
            {
                var step = journey.Steps[i];
                var mark = step.State == JourneyStepState.AlreadyInstalled ? "[bereits da]" : "[offen]";

                Console.WriteLine($"  {i + 1,2}. {step.Recipe.Name}  {mark}");
                Console.WriteLine($"        {step.Recipe.Id}  ({Describe(step.Reason)})");
            }

            Console.WriteLine();
            Console.WriteLine($"  Offen: {journey.Remaining.Count} von {journey.Steps.Count}");
        }

        Console.WriteLine();

        if (journey.Problems.Count > 0)
        {
            Console.WriteLine("  BEFUNDE");
            Console.WriteLine("  -------------");

            foreach (var problem in journey.Problems)
            {
                Console.WriteLine($"  [BLOCK] {problem.Message}");

                if (problem.Detail is not null)
                {
                    Console.WriteLine($"          {problem.Detail}");
                }
            }

            Console.WriteLine();
        }
    }

    private static string Describe(JourneyReason reason) => reason switch
    {
        JourneyReason.VersionTransition => "Versionswechsel",
        JourneyReason.Dependency => "wird vorausgesetzt",
        _ => "ausgewählt",
    };
}
