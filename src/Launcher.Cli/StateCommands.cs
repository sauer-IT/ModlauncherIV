using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Protection;
using ModlauncherIV.Core.Verification;

namespace ModlauncherIV.Cli;

/// <summary>
/// Commands about the state of an installation: what protects it against
/// updates, whether everything still sits as installed, and which path leads to
/// another game version. All three only read, with the exception of
/// <c>guard --apply</c>.
/// </summary>
internal static class StateCommands
{
    // ------------------------------------------------------------------ guard

    public static int Guard(CliOptions options, GameInstall install)
    {
        var status = UpdateGuard.Check(install);

        Console.WriteLine($"  Installation   {install.Path}");
        Console.WriteLine($"  Platform       {install.Platform}");
        Console.WriteLine();
        Console.WriteLine($"  [{Tag(status.State)}] {status.Summary}");

        if (status.Detail is not null)
        {
            Console.WriteLine($"          {status.Detail}");
        }

        if (status.ManifestPath is not null)
        {
            Console.WriteLine($"          {status.ManifestPath}");
        }

        if (status.Instructions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  HOW TO DO IT");
            Console.WriteLine("  ----------------");
            foreach (var line in status.Instructions)
            {
                Console.WriteLine($"  - {line}");
            }
        }

        if (!options.Apply)
        {
            Console.WriteLine();
            Console.WriteLine("  --apply sets the lock, if the platform has one.");
            return status.State == GuardState.Unlocked ? ExitCode.Blocked : ExitCode.Ok;
        }

        Console.WriteLine();
        if (UpdateGuard.TryLock(install, out var message))
        {
            Console.WriteLine($"  {message}");
            return ExitCode.Ok;
        }

        Console.Error.WriteLine($"  Not set: {message}");
        return ExitCode.Failed;
    }

    // ----------------------------------------------------------------- verify

    public static int Verify(GameInstall install)
    {
        var ledger = new LedgerStore(install.Path).Load();
        // Only to tell a downgrade that was taken back from a platform that
        // patched the game. Unsigned, it loads nothing and the report says the
        // more careful of the two things - which is the right way round.
        var catalog = RecipeCatalog.LoadFrom(AppPaths.CatalogDirectory, CatalogTrust.RequireSignature);
        var result = InstallVerifier.Verify(install, ledger, catalog.Recipes);

        Console.WriteLine($"  Installation   {result.GameRoot}");
        Console.WriteLine($"  Version now    {result.CurrentVersion ?? "(unknown)"}");

        if (result.ExpectedVersion is not null)
        {
            Console.WriteLine($"  Expected       {result.ExpectedVersion}");
        }

        Console.WriteLine($"  Checked        {result.Files.Count} file(s) from {ledger.Entries.Count} recipe(s)");
        Console.WriteLine();

        var auffaellig = result.Files
            .Where(f => f.State is OwnedFileState.Modified or OwnedFileState.Missing)
            .ToArray();

        if (auffaellig.Length > 0)
        {
            Console.WriteLine("  NOTABLE");
            Console.WriteLine("  ---------------");
            foreach (var file in auffaellig)
            {
                var state = file.State == OwnedFileState.Missing ? "missing " : "changed ";
                Console.WriteLine($"  {state}  {file.RelativePath,-40} from {file.RecipeId}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("  FINDINGS");
        Console.WriteLine("  -------------");
        foreach (var note in result.Notes.OrderByDescending(n => n.Level))
        {
            Console.WriteLine($"  [{Tag(note.Level)}] {note.Message}");
            if (note.Detail is not null)
            {
                Console.WriteLine($"          {note.Detail}");
            }
        }

        return result.IsIntact ? ExitCode.Ok : ExitCode.Blocked;
    }

    // ------------------------------------------------------------------ route

    public static int Route(CliOptions options, GameInstall install, CatalogLoadResult catalog)
    {
        var target = options.Argument;
        var graph = VersionGraph.Build(catalog.Recipes);
        var from = install.Version.Raw;

        Console.WriteLine($"  Current        {from}  ({install.Version.DisplayName})");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(target))
        {
            var reachable = graph.ReachableFrom(from);

            if (reachable.Count == 0)
            {
                Console.WriteLine("  No recipe leads from here to another version.");
                Console.WriteLine("  The catalog holds no matching edge.");
                return ExitCode.NothingFound;
            }

            Console.WriteLine("  REACHABLE VERSIONS");
            Console.WriteLine("  ---------------------------");
            foreach (var version in reachable)
            {
                var path = graph.FindPath(from, version);
                Console.WriteLine($"  {version,-12} {path?.Count ?? 0} step(s)");
            }

            Console.WriteLine();
            Console.WriteLine("  mliv route <version> shows the path in detail.");
            return ExitCode.Ok;
        }

        var route = graph.FindPath(from, target);

        if (route is null)
        {
            Console.Error.WriteLine($"  No path from {from} to {target}.");
            Console.Error.WriteLine("  Either a recipe is missing from the catalog, or the target version is unknown.");
            return ExitCode.NothingFound;
        }

        if (route.Count == 0)
        {
            Console.WriteLine($"  The game is already on {target}. Nothing to do.");
            return ExitCode.Ok;
        }

        Console.WriteLine($"  PATH TO {target}");
        Console.WriteLine("  " + new string('-', 20));

        for (var i = 0; i < route.Count; i++)
        {
            var edge = route[i];
            Console.WriteLine($"  {i + 1,2}. {edge.From} -> {edge.To}");
            Console.WriteLine($"      {edge.Recipe.Id}  ({edge.Recipe.Name})");
        }

        Console.WriteLine();
        Console.WriteLine($"  {route.Count} recipe(s), each with its own snapshot and its own rollback.");
        Console.WriteLine("  Run them one at a time with: mliv apply <recipe-id>");

        return ExitCode.Ok;
    }

    // ----------------------------------------------------------------- Helfer

    private static string Tag(GuardState state) => state switch
    {
        GuardState.Locked => "OK   ",
        GuardState.Unlocked => "WARN ",
        GuardState.NoSwitch => "INFO ",
        _ => "INFO ",
    };

    private static string Tag(NoteLevel level) => level switch
    {
        NoteLevel.Blocker => "BLOCK",
        NoteLevel.Warning => "WARN ",
        _ => "INFO ",
    };
}
