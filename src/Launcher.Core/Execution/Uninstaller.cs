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
/// Takes a recipe back by restoring its snapshot.
///
/// This is not "delete the files": the snapshot also knows which files existed
/// beforehand and with what content. That is the only reason a downgrade can be
/// undone in which 58 files were replaced and 88 newly created — deleting alone
/// would not bring the 58 overwritten ones back.
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

        // The same conditions as when installing: a running game locks the files,
        // and without write permission the restore fails halfway through — which
        // would leave things worse than before.
        TransactionRunner.CheckProcesses(issues);
        TransactionRunner.CheckWritable(context, issues);

        var snapshot = snapshots.Load(entry.SnapshotId);

        if (snapshot is null)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"Snapshot {entry.SnapshotId} is missing.",
                "Without it the previous state cannot be restored."));
        }

        // What depends on this recipe? Removing the ASI loader while the GFWL stub
        // still needs its plugins/ leaves half a system behind.
        foreach (var other in ledger.Entries.Where(e => !Same(e.RecipeId, recipeId)))
        {
            var recipe = catalog.FirstOrDefault(r => Same(r.Id, other.RecipeId));
            if (recipe is not null && recipe.Dependencies.Any(d => Same(d, recipeId)))
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Fatal,
                    $"{other.RecipeId} requires {recipeId} and is still installed.",
                    "Remove the dependent recipe first."));
            }
        }

        return new RemovalPlan(entry, snapshot, issues);
    }

    /// <summary>
    /// Every installed recipe in reverse order of installation.
    /// Exactly that order makes dependencies resolve themselves.
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
                ["The removal was not executed."], Lines(context));
        }

        log.Info($"Restoring snapshot {plan.Snapshot.Id} ({plan.Snapshot.Entries.Count} paths).");
        var failures = snapshots.Restore(plan.Snapshot);

        foreach (var failure in failures)
        {
            log.Error($"Not restored: {failure}");
        }

        // The ledger is cleaned up even when individual files could not be put
        // back: otherwise we keep claiming the recipe is installed when it is
        // not. The failures are reported in the outcome.
        try
        {
            var ledger = ledgerStore.Load();
            ledgerStore.Save(ledgerStore.Remove(ledger, plan.Entry.RecipeId));
            log.Info($"{plan.Entry.RecipeId} removed from the ledger.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            failures = [.. failures, $"Ledger is not writable: {e.Message}"];
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
