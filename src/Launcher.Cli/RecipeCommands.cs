using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Cli;

internal static class RecipeCommands
{
    // ---------------------------------------------------------------- catalog

    public static int ShowCatalog(CliOptions options)
    {
        var result = LoadCatalog(options);

        if (result.Recipes.Count == 0)
        {
            Console.WriteLine("No recipes found.");
            return result.Errors.Count > 0 ? ExitCode.Failed : ExitCode.NothingFound;
        }

        Console.WriteLine($"{result.Recipes.Count} recipe(s):");
        Console.WriteLine();

        foreach (var recipe in result.Recipes.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {recipe.Id}  ({recipe.Version})");
            Console.WriteLine($"      {recipe.Name}");

            if (recipe.AppliesTo.Count > 0)
            {
                Console.WriteLine($"      for version: {string.Join(", ", recipe.AppliesTo)}");
            }

            if (recipe.IsVersionTransition)
            {
                Console.WriteLine($"      produces version: {recipe.ProducesVersion}");
            }

            if (recipe.Dependencies.Count > 0)
            {
                Console.WriteLine($"      requires: {string.Join(", ", recipe.Dependencies)}");
            }

            Console.WriteLine($"      {recipe.Actions.Count} step(s), {recipe.RequiredFiles.Count} file(s)");
            Console.WriteLine();
        }

        // Even when recipes did load: an error in the catalog can mean a file was
        // tampered with. That must not pass as success.
        return result.Errors.Count > 0 ? ExitCode.Failed : ExitCode.Ok;
    }

    // ------------------------------------------------------------------- plan

    public static int Plan(CliOptions options)
    {
        var setup = Prepare(options);
        if (setup is null)
        {
            return ExitCode.NothingFound;
        }

        var (install, recipe, context, runner) = setup.Value;
        var plan = runner.Plan(recipe, context, install.Version.IsKnown ? install.Version.Raw : null);

        PrintPlan(plan, install);

        return plan.CanRun ? ExitCode.Ok : ExitCode.Blocked;
    }

    // ------------------------------------------------------------------ apply

    public static int Apply(CliOptions options)
    {
        var setup = Prepare(options);
        if (setup is null)
        {
            return ExitCode.NothingFound;
        }

        var (install, recipe, context, runner) = setup.Value;
        var plan = runner.Plan(recipe, context, install.Version.IsKnown ? install.Version.Raw : null);

        PrintPlan(plan, install);

        if (!plan.CanRun)
        {
            Console.Error.WriteLine("Nothing was executed.");
            return ExitCode.Blocked;
        }

        if (!options.Yes && !Confirm(plan))
        {
            Console.WriteLine("Cancelled. Nothing was changed.");
            return ExitCode.Ok;
        }

        var outcome = runner.Apply(plan, context);

        Console.WriteLine();
        Console.WriteLine("  LOG");
        Console.WriteLine("  ---------------");
        foreach (var line in outcome.Log)
        {
            Console.WriteLine($"  {line}");
        }

        Console.WriteLine();

        if (outcome.Success)
        {
            Console.WriteLine($"  Done. Snapshot {outcome.SnapshotId} is ready for taking this back.");
            return ExitCode.Ok;
        }

        Console.Error.WriteLine("  FAILED");
        foreach (var error in outcome.Errors)
        {
            Console.Error.WriteLine($"    {error}");
        }

        Console.Error.WriteLine(outcome.RolledBack
            ? "  The previous state was restored."
            : "  Nothing was changed.");

        return ExitCode.Failed;
    }

    // ----------------------------------------------------------------- status

    public static int Status(CliOptions options)
    {
        var install = FindInstall(options);
        if (install is null)
        {
            return ExitCode.NothingFound;
        }

        var store = new LedgerStore(install.Path);
        var ledger = store.Load();
        var snapshots = new SnapshotStore(install.Path);

        Console.WriteLine($"  Installation   {install.Path}");
        Console.WriteLine($"  Ledger         {store.FilePath}");
        Console.WriteLine($"  Backups        {snapshots.TotalSizeBytes() / 1024 / 1024} MB");
        Console.WriteLine();

        if (ledger.Entries.Count == 0)
        {
            Console.WriteLine("  The launcher has changed nothing about this installation.");
            return ExitCode.Ok;
        }

        foreach (var entry in ledger.Entries.OrderBy(e => e.InstalledAt))
        {
            Console.WriteLine($"  {entry.RecipeId}  ({entry.RecipeVersion})");
            Console.WriteLine($"      {entry.RecipeName}");
            Console.WriteLine($"      installed {entry.InstalledAt:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"      snapshot {entry.SnapshotId}, {entry.Files.Count} file(s)");
            Console.WriteLine();
        }

        return ExitCode.Ok;
    }

    // ----------------------------------------------------------------- Helfer

    private static (GameInstall Install, Recipe Recipe, RecipeContext Context, TransactionRunner Runner)?
        Prepare(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Argument))
        {
            Console.Error.WriteLine("The recipe id is missing. Available recipes: mliv catalog");
            return null;
        }

        var install = FindInstall(options);
        if (install is null)
        {
            return null;
        }

        var catalog = LoadCatalog(options);
        var recipe = catalog.Find(options.Argument);
        if (recipe is null)
        {
            Console.Error.WriteLine($"Recipe not found: {options.Argument}");
            return null;
        }

        var cache = options.CachePath ?? AppPaths.Cache;
        Directory.CreateDirectory(cache);

        var context = new RecipeContext(
            gameRoot: install.Path,
            sourceRoot: cache,
            log: new ExecutionLog(),
            dryRun: false);

        var runner = new TransactionRunner(
            new SnapshotStore(install.Path),
            new LedgerStore(install.Path));

        return (install, recipe, context, runner);
    }

    public static GameInstall? FindInstall(CliOptions options)
    {
        var installs = DetectCommand.Collect(options.GamePath, out _);

        switch (installs.Count)
        {
            case 0:
                Console.Error.WriteLine("No GTA IV installation found. Use --path to give a folder.");
                return null;

            case 1:
                return WithAssumedVersion(installs[0], options);

            default:
                Console.Error.WriteLine("Several installations found — pick one with --path:");
                foreach (var install in installs)
                {
                    Console.Error.WriteLine($"  {install.Path}");
                }

                return null;
        }
    }

    /// <summary>
    /// Replaces the measured version with a given one. Meant for the case where
    /// the EXE was swapped and its version string no longer matches — but the
    /// user takes responsibility by doing so, hence the warning.
    /// </summary>
    private static GameInstall WithAssumedVersion(GameInstall install, CliOptions options)
    {
        if (options.AssumeVersion is null)
        {
            return install;
        }

        Console.Error.WriteLine(
            $"Careful: version {options.AssumeVersion} was given, not measured "
            + $"(measured was {install.Version.Raw}).");

        return install with { Version = KnownVersions.Resolve(options.AssumeVersion) };
    }

    private static string ResolveCatalogPath(CliOptions options) =>
        options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog");

    /// <summary>
    /// Loads the catalog and reports warnings and errors on stderr. Without
    /// --allow-unsigned the signature has to be valid.
    /// </summary>
    public static CatalogLoadResult LoadCatalog(CliOptions options)
    {
        var result = RecipeCatalog.LoadFrom(
            ResolveCatalogPath(options),
            options.AllowUnsigned ? CatalogTrust.AllowUnsigned : CatalogTrust.RequireSignature,
            options.PublicKey);

        foreach (var warning in result.Warnings)
        {
            Console.Error.WriteLine($"Careful: {warning}");
        }

        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"Catalog error: {error}");
        }

        return result;
    }

    private static void PrintPlan(ExecutionPlan plan, GameInstall install)
    {
        Console.WriteLine(new string('=', 74));
        Console.WriteLine($"  {plan.Recipe.Id} — {plan.Recipe.Name}");
        Console.WriteLine(new string('=', 74));
        Console.WriteLine();
        Console.WriteLine($"  Game           {install.Path}");
        Console.WriteLine($"  Version        {install.Version.Raw}");
        Console.WriteLine($"  Recipe version {plan.Recipe.Version}");
        Console.WriteLine();

        if (plan.Recipe.Description is not null)
        {
            Console.WriteLine($"  {plan.Recipe.Description}");
            Console.WriteLine();
        }

        Console.WriteLine("  STEPS");
        Console.WriteLine("  --------------");
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            Console.WriteLine($"  {i + 1,2}. {step.Description}");

            foreach (var path in step.AffectedPaths.Take(5))
            {
                Console.WriteLine($"        -> {Path.GetRelativePath(plan.GameRoot, path)}");
            }

            if (step.AffectedPaths.Count > 5)
            {
                Console.WriteLine($"        -> ... and {step.AffectedPaths.Count - 5} more");
            }
        }

        // Named separately, because a deletion is the one thing in the plan that
        // nobody asked for by writing a recipe - it follows from what was
        // installed before, and should not turn up as a surprise.
        if (plan.Orphans.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  LEFT OVER FROM THE PREVIOUS VERSION");
            Console.WriteLine("  -----------------------------------");
            foreach (var orphan in plan.Orphans)
            {
                Console.WriteLine($"   - {Path.GetRelativePath(plan.GameRoot, orphan)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Files affected in total: {plan.AffectedPaths.Count}");
        Console.WriteLine();

        if (plan.Issues.Count > 0)
        {
            Console.WriteLine("  FINDINGS");
            Console.WriteLine("  -------------");
            foreach (var issue in plan.Issues.OrderByDescending(i => i.Severity))
            {
                var tag = issue.Severity == IssueSeverity.Fatal ? "BLOCK" : "WARN ";
                Console.WriteLine($"  [{tag}] {issue.Message}");

                if (issue.Detail is not null)
                {
                    Console.WriteLine($"          {issue.Detail}");
                }
            }

            Console.WriteLine();
        }
    }

    private static bool Confirm(ExecutionPlan plan)
    {
        Console.Write($"  {plan.AffectedPaths.Count} file(s) will be changed. Continue? [y/N] ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }
}
