using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;

namespace ModlauncherIV.Core.Execution;

public sealed record RemovalPlan(
    LedgerEntry Entry,
    Snapshot? Snapshot,
    IReadOnlyList<PreflightIssue> Issues)
{
    public bool CanRun => Snapshot is not null && !Issues.Any(i => i.Severity == IssueSeverity.Fatal);
}

/// <summary>
/// Nimmt ein Rezept zurueck, indem es dessen Snapshot zurueckspielt.
///
/// Das ist kein "Dateien loeschen": der Snapshot weiss auch, welche Dateien es
/// vorher schon gab und mit welchem Inhalt. Nur deshalb kann ein Downgrade
/// rueckgaengig gemacht werden, bei dem 58 Dateien ersetzt und 88 neu angelegt
/// wurden — Loeschen allein wuerde die 58 ueberschriebenen nicht zurueckbringen.
/// </summary>
public sealed class Uninstaller(SnapshotStore snapshots, LedgerStore ledgerStore)
{
    public RemovalPlan? Plan(string recipeId, RecipeContext context, IReadOnlyList<Recipe> catalog)
    {
        var ledger = ledgerStore.Load();
        var entry = ledger.Find(recipeId);

        if (entry is null)
        {
            return null;
        }

        var issues = new List<PreflightIssue>();

        // Dieselben Randbedingungen wie beim Einbauen: ein laufendes Spiel sperrt
        // die Dateien, und ohne Schreibrecht scheitert das Zurueckspielen mitten
        // drin — was den Zustand schlimmer machen wuerde als vorher.
        TransactionRunner.CheckProcesses(issues);
        TransactionRunner.CheckWritable(context, issues);

        var snapshot = snapshots.Load(entry.SnapshotId);

        if (snapshot is null)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"Der Snapshot {entry.SnapshotId} fehlt.",
                "Ohne ihn laesst sich der vorherige Zustand nicht wiederherstellen."));
        }

        // Wer haengt an diesem Rezept? Den ASI-Loader zu entfernen, waehrend der
        // GFWL-Stub seine plugins/ braucht, hinterlaesst ein halbes System.
        foreach (var other in ledger.Entries.Where(e => !Same(e.RecipeId, recipeId)))
        {
            var recipe = catalog.FirstOrDefault(r => Same(r.Id, other.RecipeId));
            if (recipe is not null && recipe.Dependencies.Any(d => Same(d, recipeId)))
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Fatal,
                    $"{other.RecipeId} setzt {recipeId} voraus und ist noch installiert.",
                    "Zuerst das abhaengige Rezept entfernen."));
            }
        }

        return new RemovalPlan(entry, snapshot, issues);
    }

    /// <summary>
    /// Alle installierten Rezepte in umgekehrter Installationsreihenfolge.
    /// Genau so loesen sich Abhaengigkeiten von selbst auf.
    /// </summary>
    public IReadOnlyList<string> InstalledNewestFirst() =>
        ledgerStore.Load().Entries
            .OrderByDescending(e => e.InstalledAt)
            .Select(e => e.RecipeId)
            .ToArray();

    public ExecutionOutcome Remove(RemovalPlan plan, RecipeContext context)
    {
        var log = context.Log;

        if (!plan.CanRun || plan.Snapshot is null)
        {
            return new ExecutionOutcome(
                false, plan.Entry.SnapshotId, false,
                ["Der Rueckbau wurde nicht ausgefuehrt."], Lines(context));
        }

        log.Info($"Spiele Snapshot {plan.Snapshot.Id} zurueck ({plan.Snapshot.Entries.Count} Pfade).");
        var failures = snapshots.Restore(plan.Snapshot);

        foreach (var failure in failures)
        {
            log.Error($"Nicht wiederhergestellt: {failure}");
        }

        // Das Ledger wird auch dann bereinigt, wenn einzelne Dateien nicht
        // zurueckkonnten: sonst behaupten wir weiter, das Rezept sei installiert,
        // obwohl es das nicht mehr ist. Die Fehler stehen im Ergebnis.
        try
        {
            var ledger = ledgerStore.Load();
            ledgerStore.Save(ledgerStore.Remove(ledger, plan.Entry.RecipeId));
            log.Info($"{plan.Entry.RecipeId} aus dem Ledger entfernt.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            failures = [.. failures, $"Ledger nicht schreibbar: {e.Message}"];
        }

        return new ExecutionOutcome(
            failures.Count == 0,
            plan.Snapshot.Id,
            RolledBack: true,
            failures.ToArray(),
            Lines(context));
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Lines(RecipeContext context) =>
        context.Log is ExecutionLog log ? log.Lines : [];
}
