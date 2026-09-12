using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Protection;
using ModlauncherIV.Core.Verification;

namespace ModlauncherIV.Cli;

/// <summary>
/// Befehle rund um den Zustand einer Installation: was sie gegen Updates
/// schützt, ob noch alles so liegt wie eingebaut, und welcher Weg zu einer
/// anderen Spielversion führt. Alle drei lesen nur, mit Ausnahme von
/// <c>guard --apply</c>.
/// </summary>
internal static class StateCommands
{
    // ------------------------------------------------------------------ guard

    public static int Guard(CliOptions options, GameInstall install)
    {
        var status = UpdateGuard.Check(install);

        Console.WriteLine($"  Installation   {install.Path}");
        Console.WriteLine($"  Plattform      {install.Platform}");
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
            Console.WriteLine("  SO GEHT ES");
            Console.WriteLine("  ----------------");
            foreach (var line in status.Instructions)
            {
                Console.WriteLine($"  - {line}");
            }
        }

        if (!options.Apply)
        {
            Console.WriteLine();
            Console.WriteLine("  Mit --apply wird die Sperre gesetzt, sofern die Plattform eine kennt.");
            return status.State == GuardState.Unlocked ? ExitCode.Blocked : ExitCode.Ok;
        }

        Console.WriteLine();
        if (UpdateGuard.TryLock(install, out var message))
        {
            Console.WriteLine($"  {message}");
            return ExitCode.Ok;
        }

        Console.Error.WriteLine($"  Nicht gesetzt: {message}");
        return ExitCode.Failed;
    }

    // ----------------------------------------------------------------- verify

    public static int Verify(GameInstall install)
    {
        var ledger = new LedgerStore(install.Path).Load();
        var result = InstallVerifier.Verify(install, ledger);

        Console.WriteLine($"  Installation   {result.GameRoot}");
        Console.WriteLine($"  Version jetzt  {result.CurrentVersion ?? "(unbekannt)"}");

        if (result.ExpectedVersion is not null)
        {
            Console.WriteLine($"  Erwartet       {result.ExpectedVersion}");
        }

        Console.WriteLine($"  Geprüft        {result.Files.Count} Datei(en) aus {ledger.Entries.Count} Rezept(en)");
        Console.WriteLine();

        var auffaellig = result.Files
            .Where(f => f.State is OwnedFileState.Modified or OwnedFileState.Missing)
            .ToArray();

        if (auffaellig.Length > 0)
        {
            Console.WriteLine("  AUFFÄLLIG");
            Console.WriteLine("  ---------------");
            foreach (var file in auffaellig)
            {
                var state = file.State == OwnedFileState.Missing ? "fehlt   " : "verändert";
                Console.WriteLine($"  {state}  {file.RelativePath,-40} aus {file.RecipeId}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("  BEFUNDE");
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

        Console.WriteLine($"  Ist-Version    {from}  ({install.Version.DisplayName})");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(target))
        {
            var reachable = graph.ReachableFrom(from);

            if (reachable.Count == 0)
            {
                Console.WriteLine("  Von hier aus führt kein Rezept zu einer anderen Version.");
                Console.WriteLine("  Der Katalog enthält keine passende Kante.");
                return ExitCode.NothingFound;
            }

            Console.WriteLine("  ERREICHBARE VERSIONEN");
            Console.WriteLine("  ---------------------------");
            foreach (var version in reachable)
            {
                var path = graph.FindPath(from, version);
                Console.WriteLine($"  {version,-12} {path?.Count ?? 0} Schritt(e)");
            }

            Console.WriteLine();
            Console.WriteLine("  mliv route <version> zeigt den Weg im Einzelnen.");
            return ExitCode.Ok;
        }

        var route = graph.FindPath(from, target);

        if (route is null)
        {
            Console.Error.WriteLine($"  Kein Weg von {from} nach {target}.");
            Console.Error.WriteLine("  Entweder fehlt ein Rezept im Katalog, oder die Zielversion ist unbekannt.");
            return ExitCode.NothingFound;
        }

        if (route.Count == 0)
        {
            Console.WriteLine($"  Das Spiel ist bereits auf {target}. Nichts zu tun.");
            return ExitCode.Ok;
        }

        Console.WriteLine($"  WEG NACH {target}");
        Console.WriteLine("  " + new string('-', 20));

        for (var i = 0; i < route.Count; i++)
        {
            var edge = route[i];
            Console.WriteLine($"  {i + 1,2}. {edge.From} -> {edge.To}");
            Console.WriteLine($"      {edge.Recipe.Id}  ({edge.Recipe.Name})");
        }

        Console.WriteLine();
        Console.WriteLine($"  {route.Count} Rezept(e), jedes mit eigenem Snapshot und eigenem Rollback.");
        Console.WriteLine("  Ausführen einzeln mit: mliv apply <rezept-id>");

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
