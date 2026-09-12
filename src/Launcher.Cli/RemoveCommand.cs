using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Cli;

/// <summary>
/// Takes recipes back out. With <c>--all</c> in reverse installation order, so
/// that dependencies resolve themselves.
/// </summary>
internal static class RemoveCommand
{
    public static int Run(CliOptions options, GameInstall install, CatalogLoadResult catalog)
    {
        var uninstaller = new Uninstaller(
            new SnapshotStore(install.Path),
            new LedgerStore(install.Path));

        var context = new RecipeContext(
            gameRoot: install.Path,
            sourceRoot: options.CachePath ?? AppPaths.Cache,
            log: new ExecutionLog(),
            dryRun: false);

        var targets = options.All || string.IsNullOrWhiteSpace(options.Argument)
            ? uninstaller.InstalledNewestFirst()
            : [options.Argument];

        if (targets.Count == 0)
        {
            Console.WriteLine("  The launcher has installed nothing here.");
            return ExitCode.Ok;
        }

        if (string.IsNullOrWhiteSpace(options.Argument) && !options.All)
        {
            Console.Error.WriteLine("The recipe id is missing. To take everything back: mliv remove --all");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Currently installed:");
            foreach (var id in targets)
            {
                Console.Error.WriteLine($"  {id}");
            }

            return ExitCode.BadUsage;
        }

        Console.WriteLine($"  Installation   {install.Path}");
        Console.WriteLine($"  To remove      {targets.Count} recipe(s), newest first:");
        foreach (var id in targets)
        {
            Console.WriteLine($"      {id}");
        }

        Console.WriteLine();

        if (!options.Yes && !Confirm(targets.Count))
        {
            Console.WriteLine("  Cancelled. Nothing was changed.");
            return ExitCode.Ok;
        }

        var failed = false;

        foreach (var id in targets)
        {
            Console.WriteLine($"  --- {id} ---");

            var plan = uninstaller.Plan(id, context, catalog.Recipes);
            if (plan is null)
            {
                Console.Error.WriteLine($"      Not installed: {id}");
                failed = true;
                continue;
            }

            foreach (var issue in plan.Issues.OrderByDescending(i => i.Severity))
            {
                var tag = issue.Severity == IssueSeverity.Fatal ? "BLOCK" : "WARN ";
                Console.WriteLine($"      [{tag}] {issue.Message}");
                if (issue.Detail is not null)
                {
                    Console.WriteLine($"              {issue.Detail}");
                }
            }

            if (!plan.CanRun)
            {
                Console.Error.WriteLine($"      Skipped.");
                failed = true;
                continue;
            }

            var outcome = uninstaller.Remove(plan, context);

            if (outcome.Success)
            {
                Console.WriteLine($"      Zurueckgebaut ({plan.Snapshot!.Entries.Count} Pfade).");
            }
            else
            {
                failed = true;
                Console.Error.WriteLine("      MIT FEHLERN:");
                foreach (var error in outcome.Errors)
                {
                    Console.Error.WriteLine($"        {error}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("  To check: mliv detect  and  mliv status");

        return failed ? ExitCode.Failed : ExitCode.Ok;
    }

    private static bool Confirm(int count)
    {
        Console.Write($"  Remove {count} recipe(s)? [y/N] ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().StartsWith('y');
    }
}
