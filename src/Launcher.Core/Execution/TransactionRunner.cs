using System.Diagnostics;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;

namespace ModlauncherIV.Core.Execution;

public enum IssueSeverity
{
    Warning,
    Fatal,
}

public sealed record PreflightIssue(IssueSeverity Severity, string Message, string? Detail = null);

/// <summary>A step together with what it will touch — known before execution.</summary>
public sealed record PlannedStep(
    RecipeStep Step,
    string Description,
    IReadOnlyList<string> AffectedPaths);

/// <summary>
/// The fully resolved plan. Produced without changing the game at all, and at
/// the same time the output of the dry run — what is listed here is exactly
/// what would happen.
/// </summary>
public sealed record ExecutionPlan(
    Recipe Recipe,
    string GameRoot,
    IReadOnlyList<PlannedStep> Steps,
    IReadOnlyList<PreflightIssue> Issues,
    IReadOnlyList<string> MissingSources,
    IReadOnlyList<string> Orphans)
{
    /// <summary>
    /// Everything the snapshot has to cover: what the steps touch, plus the
    /// files left over from the previous install.
    ///
    /// The orphans belong in here and not only in the deletion, or a rollback
    /// would restore the new state and leave the old files gone for good.
    /// </summary>
    public IReadOnlyList<string> AffectedPaths => Steps
        .SelectMany(s => s.AffectedPaths)
        .Concat(Orphans)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool CanRun => !Issues.Any(i => i.Severity == IssueSeverity.Fatal);
}

public sealed record ExecutionOutcome(
    bool Success,
    string? SnapshotId,
    bool RolledBack,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Log);

/// <summary>
/// Runs recipes — always in the same order:
/// pre-flight, snapshot, apply, verify, commit. If anything fails the snapshot
/// is restored and the ledger is left untouched.
/// </summary>
public sealed class TransactionRunner(SnapshotStore snapshots, LedgerStore ledgerStore)
{
    /// <summary>Process names that forbid writing while they are running.</summary>
    private static readonly string[] BlockingProcesses = ["GTAIV", "PlayGTAIV", "LaunchGTAIV"];

    // ------------------------------------------------------------------ Plan

    /// <summary>
    /// Builds the plan. Read-only — this method must not change anything, or the
    /// dry run would be worthless.
    /// </summary>
    public ExecutionPlan Plan(Recipe recipe, RecipeContext context, string? installedVersion)
    {
        var issues = new List<PreflightIssue>();
        var steps = new List<PlannedStep>();

        CheckVersion(recipe, installedVersion, issues);
        CheckLedger(recipe, issues);
        var missingSources = CheckSources(recipe, context, issues);
        CheckProcesses(issues);

        foreach (var step in recipe.Actions)
        {
            try
            {
                steps.Add(new PlannedStep(step, step.Describe(), step.AffectedGamePaths(context)));
            }
            catch (RecipeSecurityException e)
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Fatal,
                    $"Recipe {recipe.Id} contains a path that is not allowed.",
                    e.Message));
            }
        }

        CheckDiskSpace(recipe, context, steps, issues);
        CheckWritable(context, issues);

        var orphans = FindOrphans(recipe, context, steps);

        return new ExecutionPlan(recipe, context.GameRoot, steps, issues, missingSources, orphans);
    }

    /// <summary>
    /// Files the previous install of this recipe owned and the new one no longer
    /// writes.
    ///
    /// Updating a recipe overwrites what keeps its name. What gets renamed, or
    /// dropped, would otherwise stay behind forever - and for an ASI that is not
    /// cosmetic: two plugins in the same folder means the game loads both, and
    /// the older one answers on the same key as the newer. That is exactly what
    /// happened when the trainer was renamed to sauer.
    ///
    /// The ledger already knows which files the previous version created, so
    /// nothing has to be guessed and no list of old names has to be maintained.
    /// This works for every recipe, not just for the one that prompted it.
    /// </summary>
    private IReadOnlyList<string> FindOrphans(
        Recipe recipe, RecipeContext context, List<PlannedStep> steps)
    {
        InstallLedger ledger;
        try
        {
            ledger = ledgerStore.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable ledger is already reported by CheckLedger. Cleaning
            // up is the lesser duty of the two - it must not add a second
            // message about the same problem.
            return [];
        }

        var previous = ledger.Find(recipe.Id);
        if (previous is null)
        {
            return [];
        }

        var keeps = steps
            .SelectMany(s => s.AffectedPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Anything another recipe owns as well is not ours to delete. Mods do
        // overwrite each other's files - FusionFix ships its own dinput8.dll
        // over the one the ASI loader installed - and dropping it on an update
        // would quietly take the other mod apart.
        foreach (var other in ledger.Entries.Where(e => !string.Equals(
                     e.RecipeId, recipe.Id, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var file in other.Files)
            {
                try
                {
                    keeps.Add(context.ResolveGamePath(file.RelativePath));
                }
                catch (RecipeSecurityException)
                {
                    // Not a path in the game directory, so not a path we would
                    // have deleted either.
                }
            }
        }

        var orphans = new List<string>();

        foreach (var owned in previous.Files)
        {
            string path;
            try
            {
                // Through the containment check like everything else: a ledger is
                // a file too, and a path out of the game directory must not
                // become a deletion just because it once got written down.
                path = context.ResolveGamePath(owned.RelativePath);
            }
            catch (RecipeSecurityException)
            {
                continue;
            }

            if (!keeps.Contains(path) && File.Exists(path))
            {
                orphans.Add(path);
            }
        }

        return orphans;
    }

    private static void CheckVersion(Recipe recipe, string? installedVersion, List<PreflightIssue> issues)
    {
        if (installedVersion is null)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Warning,
                "The game version is unknown — the recipe's suitability was not checked."));
            return;
        }

        if (!recipe.Matches(installedVersion))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"Recipe {recipe.Id} does not fit version {installedVersion}.",
                $"Allowed: {string.Join(", ", recipe.AppliesTo)}"));
        }
    }

    private void CheckLedger(Recipe recipe, List<PreflightIssue> issues)
    {
        InstallLedger ledger;
        try
        {
            ledger = ledgerStore.Load();
        }
        catch (InvalidOperationException e)
        {
            issues.Add(new PreflightIssue(IssueSeverity.Fatal, "Ledger is not readable.", e.Message));
            return;
        }

        if (ledger.IsInstalled(recipe.Id))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Warning,
                $"{recipe.Id} is already installed and will be replaced."));
        }

        foreach (var conflict in recipe.Conflicts.Where(ledger.IsInstalled))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{recipe.Id} conflicts with the installed {conflict}.",
                "Remove the other recipe first."));
        }

        foreach (var dependency in recipe.Dependencies.Where(d => !ledger.IsInstalled(d)))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{recipe.Id} requires {dependency}, which is not installed."));
        }
    }

    /// <summary>Checks the acquired files. Without a matching checksum a file counts as missing.</summary>
    private static IReadOnlyList<string> CheckSources(
        Recipe recipe,
        RecipeContext context,
        List<PreflightIssue> issues)
    {
        var missing = new List<string>();

        foreach (var source in recipe.RequiredFiles)
        {
            string path;
            try
            {
                path = context.ResolveSourcePath(source.FileName);
            }
            catch (RecipeSecurityException e)
            {
                issues.Add(new PreflightIssue(IssueSeverity.Fatal, "Source file name is not allowed.", e.Message));
                missing.Add(source.FileName);
                continue;
            }

            if (!File.Exists(path))
            {
                missing.Add(source.FileName);
                continue;
            }

            var actual = Hashing.Sha256File(path);
            if (!Hashing.Equal(actual, source.Sha256))
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Fatal,
                    $"Checksum does not match: {source.FileName}",
                    $"expected {source.Sha256}, found {actual}"));
                missing.Add(source.FileName);
            }
        }

        if (missing.Count > 0)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{missing.Count} required file(s) are missing or unverified.",
                string.Join(", ", missing)));
        }

        return missing;
    }

    /// <summary>Removal must not run while the game is open either.</summary>
    public static void CheckProcesses(List<PreflightIssue> issues)
    {
        foreach (var name in BlockingProcesses)
        {
            Process[] running;
            try
            {
                running = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            try
            {
                if (running.Length > 0)
                {
                    issues.Add(new PreflightIssue(
                        IssueSeverity.Fatal,
                        $"{name} is currently running.",
                        "The game has to be closed, otherwise the files are locked."));
                }
            }
            finally
            {
                foreach (var process in running)
                {
                    process.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// The snapshot costs as much space as the files it preserves. That has to be
    /// checked beforehand — running out of space mid-run is exactly the state the
    /// whole pipeline exists to prevent.
    /// </summary>
    private static void CheckDiskSpace(
        Recipe recipe,
        RecipeContext context,
        IReadOnlyList<PlannedStep> steps,
        List<PreflightIssue> issues)
    {
        long snapshotBytes = 0;

        foreach (var path in steps.SelectMany(s => s.AffectedPaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    snapshotBytes += new FileInfo(path).Length;
                }
            }
            catch (IOException)
            {
                // Not readable: then it simply does not count towards the total.
            }
        }

        var payloadBytes = recipe.RequiredFiles.Sum(s => s.SizeBytes);
        var needed = snapshotBytes + payloadBytes;

        foreach (var (root, label) in new[]
                 {
                     (AppPaths.Root, "snapshot directory"),
                     (context.GameRoot, "game directory"),
                 })
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
                if (drive.AvailableFreeSpace < needed)
                {
                    issues.Add(new PreflightIssue(
                        IssueSeverity.Fatal,
                        $"Not enough space on {drive.Name} ({label}).",
                        $"needs about {needed / 1024 / 1024} MB, {drive.AvailableFreeSpace / 1024 / 1024} MB free"));
                }
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Warning,
                    $"Free space for the {label} could not be determined.",
                    e.Message));
            }
        }
    }

    // ----------------------------------------------------------------- Apply

    public ExecutionOutcome Apply(ExecutionPlan plan, RecipeContext context)
    {
        var log = context.Log;
        var errors = new List<string>();

        if (!plan.CanRun)
        {
            return Fail(context, ["The plan contains blockers and was not executed."], null, false);
        }

        if (context.DryRun)
        {
            return Fail(context, ["Dry run: nothing was executed."], null, false) with { Success = true };
        }

        // --- Phase 1: pre-flight, this time immediately before writing --------
        var lateIssues = new List<PreflightIssue>();
        CheckProcesses(lateIssues);
        if (lateIssues.Any(i => i.Severity == IssueSeverity.Fatal))
        {
            return Fail(context, lateIssues.Select(i => i.Message).ToArray(), null, false);
        }

        // --- Phase 2: Snapshot ------------------------------------------------
        Snapshot snapshot;
        try
        {
            log.Info($"Backing up {plan.AffectedPaths.Count} path(s) before changing anything.");
            snapshot = snapshots.Create(plan.AffectedPaths);
            log.Info($"Snapshot {snapshot.Id} created.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(context, [$"Snapshot failed: {e.Message}"], null, false);
        }

        // --- Phase 3: Apply ---------------------------------------------------
        var applied = 0;
        foreach (var planned in plan.Steps)
        {
            try
            {
                planned.Step.Apply(context);
                applied++;
            }
            catch (Exception e) when (e is IOException
                                           or UnauthorizedAccessException
                                           or FileNotFoundException
                                           or RecipeSecurityException)
            {
                errors.Add($"Step {applied + 1} ({planned.Description}) failed: {e.Message}");
                break;
            }
        }

        // Leftovers from the previous version of this recipe, once the new one
        // is in place. After the steps rather than before: if a step fails we
        // roll back anyway, and there is never a moment where the old file is
        // gone and the new one is not there yet.
        if (errors.Count == 0)
        {
            foreach (var orphan in plan.Orphans)
            {
                try
                {
                    if (File.Exists(orphan))
                    {
                        File.Delete(orphan);
                        log.Info($"Left over from the previous version, removed: " +
                                 $"{Path.GetRelativePath(context.GameRoot, orphan)}");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"Could not remove the leftover {orphan}: {e.Message}");
                    break;
                }
            }
        }

        // --- Phase 4: Verify --------------------------------------------------
        if (errors.Count == 0)
        {
            foreach (var planned in plan.Steps)
            {
                try
                {
                    errors.AddRange(planned.Step.Verify(context));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"Verification of '{planned.Description}' failed: {e.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            log.Error($"{errors.Count} error(s) — rolling back.");
            var failures = snapshots.Restore(snapshot);

            foreach (var failure in failures)
            {
                log.Error($"Rollback incomplete: {failure}");
            }

            errors.AddRange(failures.Select(f => $"Rollback: {f}"));
            return new ExecutionOutcome(false, snapshot.Id, RolledBack: true, errors, LogLines(context));
        }

        // --- Phase 5: Commit --------------------------------------------------
        try
        {
            var ledger = ledgerStore.Load();
            var owned = plan.AffectedPaths
                .Where(File.Exists)
                .Select(p => new OwnedFile(
                    Path.GetRelativePath(context.GameRoot, p),
                    Hashing.Sha256File(p)))
                .ToArray();

            ledgerStore.Save(ledgerStore.Add(ledger, new LedgerEntry(
                RecipeId: plan.Recipe.Id,
                RecipeName: plan.Recipe.Name,
                RecipeVersion: plan.Recipe.Version,
                InstalledAt: DateTimeOffset.Now,
                SnapshotId: snapshot.Id,
                Files: owned,
                // The version is read again rather than taken from the recipe:
                // what actually sits in the directory is the truth.
                GameVersionAfter: ReadGameVersion(context.GameRoot))));

            log.Info($"{plan.Recipe.Id} recorded ({owned.Length} file(s)).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The change is in place but we cannot record it. That is a state we
            // must not leave behind: without a ledger entry there is no way
            // back later.
            log.Error($"Ledger is not writable: {e.Message} — rolling back.");
            var failures = snapshots.Restore(snapshot);
            errors.Add($"Ledger is not writable: {e.Message}");
            errors.AddRange(failures.Select(f => $"Rollback: {f}"));
            return new ExecutionOutcome(false, snapshot.Id, RolledBack: true, errors, LogLines(context));
        }

        return new ExecutionOutcome(true, snapshot.Id, RolledBack: false, [], LogLines(context));
    }

    private static ExecutionOutcome Fail(
        RecipeContext context,
        IReadOnlyList<string> errors,
        string? snapshotId,
        bool rolledBack) =>
        new(false, snapshotId, rolledBack, errors, LogLines(context));

    /// <summary>
    /// Determines whether we may write into the game directory at all.
    ///
    /// Deliberately without a write probe: the plan must not change anything, not
    /// even a temporary file. Checked instead is the combination that actually
    /// occurs — a protected path plus missing elevation. Without this check,
    /// apply would fail halfway through extracting and roll back: that works,
    /// but it is the needlessly expensive way to find out.
    /// </summary>
    public static void CheckWritable(RecipeContext context, List<PreflightIssue> issues)
    {
        string[] protectedRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ];

        var inProtected = protectedRoots
            .Where(r => !string.IsNullOrEmpty(r))
            .Any(r => context.GameRoot.StartsWith(
                r.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

        if (!inProtected)
        {
            return;
        }

        var environment = Detection.SystemEnvironmentProbe.Probe();
        if (environment.IsElevated)
        {
            return;
        }

        issues.Add(new PreflightIssue(
            IssueSeverity.Fatal,
            "The game sits in a protected directory and the launcher is running without administrator rights.",
            "Writing would fail halfway through. Start the launcher as administrator."));
    }

    /// <summary>Reads the game version from the EXE. Null when it is not readable.</summary>
    public static string? ReadGameVersion(string gameRoot)
    {
        try
        {
            var exe = Path.Combine(gameRoot, Detection.InstallInspector.ExecutableName);
            if (!File.Exists(exe))
            {
                return null;
            }

            // Store it canonically: the raw form varies between "1.0.7.0" and
            // "1, 0, 7, 0" depending on the binary.
            return Detection.KnownVersions.Normalise(FileVersionInfo.GetVersionInfo(exe).FileVersion);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> LogLines(RecipeContext context) =>
        context.Log is ExecutionLog log ? log.Lines : [];
}
