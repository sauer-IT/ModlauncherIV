using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.Cli;

/// <summary>
/// Shows the path from the installation as found to a wanted state.
///
/// This is the same plan the wizard works through later. It hangs off the CLI
/// because a window cannot be tested against fixtures and the ordering logic is
/// too important to check by hand only.
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

        // Several recipes separated by commas. An empty list is allowed: then you
        // are only asking about the version change.
        var wanted = (options.Argument ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var journey = JourneyPlanner.Plan(
            new JourneyRequest(options.TargetVersion, wanted),
            catalog.Recipes,
            install.Version.Raw,
            new LedgerStore(install.Path).Load());

        Print(journey, install);

        // A path with open steps is not an error but the normal case —
        // this command plans, it does not execute.
        return journey.IsPossible ? ExitCode.Ok : ExitCode.Blocked;
    }

    private static void Print(Journey journey, GameInstall install)
    {
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("  Path to the wanted state");
        Console.WriteLine(new string('=', 74));
        Console.WriteLine();
        Console.WriteLine($"  Game           {install.Path}");
        Console.WriteLine($"  Now            {journey.FromVersion}");
        Console.WriteLine($"  Target         {journey.TargetVersion}");
        Console.WriteLine();

        if (journey.Steps.Count == 0)
        {
            Console.WriteLine("  There is nothing to do.");
        }
        else
        {
            Console.WriteLine("  STEPS");
            Console.WriteLine("  --------------");

            for (var i = 0; i < journey.Steps.Count; i++)
            {
                var step = journey.Steps[i];

                var mark = step.State switch
                {
                    JourneyStepState.AlreadyInstalled => "[already there]",
                    JourneyStepState.NeedsUpdate => $"[update {step.InstalledVersion} -> {step.Recipe.Version}]",
                    _ => "[open]",
                };

                Console.WriteLine($"  {i + 1,2}. {step.Recipe.Name}  {mark}");
                Console.WriteLine($"        {step.Recipe.Id}  ({Describe(step.Reason)})");
            }

            Console.WriteLine();
            Console.WriteLine($"  Open: {journey.Remaining.Count} of {journey.Steps.Count}");
        }

        Console.WriteLine();

        if (journey.Problems.Count > 0)
        {
            Console.WriteLine("  FINDINGS");
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
        JourneyReason.VersionTransition => "version change",
        JourneyReason.Dependency => "required by another",
        _ => "selected",
    };
}
