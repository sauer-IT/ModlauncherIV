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
        var result = RecipeCatalog.LoadFrom(ResolveCatalogPath(options));

        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"Katalogfehler: {error}");
        }

        if (result.Recipes.Count == 0)
        {
            Console.WriteLine("Keine Rezepte gefunden.");
            return result.Errors.Count > 0 ? ExitCode.Failed : ExitCode.NothingFound;
        }

        Console.WriteLine($"{result.Recipes.Count} Rezept(e):");
        Console.WriteLine();

        foreach (var recipe in result.Recipes.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {recipe.Id}  ({recipe.Version})");
            Console.WriteLine($"      {recipe.Name}");

            if (recipe.AppliesTo.Count > 0)
            {
                Console.WriteLine($"      für Version: {string.Join(", ", recipe.AppliesTo)}");
            }

            if (recipe.IsVersionTransition)
            {
                Console.WriteLine($"      erzeugt Version: {recipe.ProducesVersion}");
            }

            if (recipe.Dependencies.Count > 0)
            {
                Console.WriteLine($"      benötigt: {string.Join(", ", recipe.Dependencies)}");
            }

            Console.WriteLine($"      {recipe.Actions.Count} Schritt(e), {recipe.RequiredFiles.Count} Datei(en)");
            Console.WriteLine();
        }

        return ExitCode.Ok;
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
            Console.Error.WriteLine("Es wurde nichts ausgeführt.");
            return ExitCode.Blocked;
        }

        if (!options.Yes && !Confirm(plan))
        {
            Console.WriteLine("Abgebrochen. Es wurde nichts verändert.");
            return ExitCode.Ok;
        }

        var outcome = runner.Apply(plan, context);

        Console.WriteLine();
        Console.WriteLine("  PROTOKOLL");
        Console.WriteLine("  ---------------");
        foreach (var line in outcome.Log)
        {
            Console.WriteLine($"  {line}");
        }

        Console.WriteLine();

        if (outcome.Success)
        {
            Console.WriteLine($"  Fertig. Snapshot {outcome.SnapshotId} liegt bereit für den Rückbau.");
            return ExitCode.Ok;
        }

        Console.Error.WriteLine("  FEHLGESCHLAGEN");
        foreach (var error in outcome.Errors)
        {
            Console.Error.WriteLine($"    {error}");
        }

        Console.Error.WriteLine(outcome.RolledBack
            ? "  Der vorherige Zustand wurde wiederhergestellt."
            : "  Es wurde nichts verändert.");

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
        Console.WriteLine($"  Sicherungen    {snapshots.TotalSizeBytes() / 1024 / 1024} MB");
        Console.WriteLine();

        if (ledger.Entries.Count == 0)
        {
            Console.WriteLine("  Der Launcher hat an dieser Installation nichts verändert.");
            return ExitCode.Ok;
        }

        foreach (var entry in ledger.Entries.OrderBy(e => e.InstalledAt))
        {
            Console.WriteLine($"  {entry.RecipeId}  ({entry.RecipeVersion})");
            Console.WriteLine($"      {entry.RecipeName}");
            Console.WriteLine($"      installiert {entry.InstalledAt:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"      Snapshot {entry.SnapshotId}, {entry.Files.Count} Datei(en)");
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
            Console.Error.WriteLine("Es fehlt die Rezept-ID. Verfügbare Rezepte: mliv catalog");
            return null;
        }

        var install = FindInstall(options);
        if (install is null)
        {
            return null;
        }

        var catalog = RecipeCatalog.LoadFrom(ResolveCatalogPath(options));
        foreach (var error in catalog.Errors)
        {
            Console.Error.WriteLine($"Katalogfehler: {error}");
        }

        var recipe = catalog.Find(options.Argument);
        if (recipe is null)
        {
            Console.Error.WriteLine($"Rezept nicht gefunden: {options.Argument}");
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

    private static GameInstall? FindInstall(CliOptions options)
    {
        var installs = DetectCommand.Collect(options.GamePath, out _);

        switch (installs.Count)
        {
            case 0:
                Console.Error.WriteLine("Keine GTA-IV-Installation gefunden. Mit --path einen Ordner angeben.");
                return null;

            case 1:
                return installs[0];

            default:
                Console.Error.WriteLine("Mehrere Installationen gefunden — bitte mit --path eine auswählen:");
                foreach (var install in installs)
                {
                    Console.Error.WriteLine($"  {install.Path}");
                }

                return null;
        }
    }

    private static string ResolveCatalogPath(CliOptions options) =>
        options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog");

    private static void PrintPlan(ExecutionPlan plan, GameInstall install)
    {
        Console.WriteLine(new string('=', 74));
        Console.WriteLine($"  {plan.Recipe.Id} — {plan.Recipe.Name}");
        Console.WriteLine(new string('=', 74));
        Console.WriteLine();
        Console.WriteLine($"  Spiel          {install.Path}");
        Console.WriteLine($"  Version        {install.Version.Raw}");
        Console.WriteLine($"  Rezeptversion  {plan.Recipe.Version}");
        Console.WriteLine();

        if (plan.Recipe.Description is not null)
        {
            Console.WriteLine($"  {plan.Recipe.Description}");
            Console.WriteLine();
        }

        Console.WriteLine("  SCHRITTE");
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
                Console.WriteLine($"        -> ... und {step.AffectedPaths.Count - 5} weitere");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Betroffene Dateien insgesamt: {plan.AffectedPaths.Count}");
        Console.WriteLine();

        if (plan.Issues.Count > 0)
        {
            Console.WriteLine("  BEFUNDE");
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
        Console.Write($"  {plan.AffectedPaths.Count} Datei(en) werden verändert. Fortfahren? [j/N] ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().StartsWith('j');
    }
}
