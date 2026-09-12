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

/// <summary>Ein Schritt mitsamt dem, was er anfassen wird — vor der Ausführung bekannt.</summary>
public sealed record PlannedStep(
    RecipeStep Step,
    string Description,
    IReadOnlyList<string> AffectedPaths);

/// <summary>
/// Der fertig aufgelöste Plan. Entsteht ohne jede Änderung am Spiel und ist
/// zugleich die Ausgabe des Dry-Runs — was hier steht, ist genau das, was
/// passieren würde.
/// </summary>
public sealed record ExecutionPlan(
    Recipe Recipe,
    string GameRoot,
    IReadOnlyList<PlannedStep> Steps,
    IReadOnlyList<PreflightIssue> Issues,
    IReadOnlyList<string> MissingSources)
{
    public IReadOnlyList<string> AffectedPaths => Steps
        .SelectMany(s => s.AffectedPaths)
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
/// Führt Rezepte aus — immer in derselben Reihenfolge:
/// Pre-Flight, Snapshot, Apply, Verify, Commit. Schlägt irgendetwas fehl, wird
/// der Snapshot zurückgespielt und das Ledger bleibt unangetastet.
/// </summary>
public sealed class TransactionRunner(SnapshotStore snapshots, LedgerStore ledgerStore)
{
    /// <summary>Prozessnamen, bei denen nicht geschrieben werden darf.</summary>
    private static readonly string[] BlockingProcesses = ["GTAIV", "PlayGTAIV", "LaunchGTAIV"];

    // ------------------------------------------------------------------ Plan

    /// <summary>
    /// Baut den Plan. Rein lesend — diese Methode darf nichts verändern, sonst
    /// wäre der Dry-Run wertlos.
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
                    $"Rezept {recipe.Id} enthält einen unzulässigen Pfad.",
                    e.Message));
            }
        }

        CheckDiskSpace(recipe, context, steps, issues);
        CheckWritable(context, issues);

        return new ExecutionPlan(recipe, context.GameRoot, steps, issues, missingSources);
    }

    private static void CheckVersion(Recipe recipe, string? installedVersion, List<PreflightIssue> issues)
    {
        if (installedVersion is null)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Warning,
                "Die Spielversion ist unbekannt — die Eignung des Rezepts wurde nicht geprüft."));
            return;
        }

        if (!recipe.Matches(installedVersion))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"Rezept {recipe.Id} passt nicht zu Version {installedVersion}.",
                $"Erlaubt: {string.Join(", ", recipe.AppliesTo)}"));
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
            issues.Add(new PreflightIssue(IssueSeverity.Fatal, "Ledger nicht lesbar.", e.Message));
            return;
        }

        if (ledger.IsInstalled(recipe.Id))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Warning,
                $"{recipe.Id} ist bereits installiert und wird ersetzt."));
        }

        foreach (var conflict in recipe.Conflicts.Where(ledger.IsInstalled))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{recipe.Id} verträgt sich nicht mit dem installierten {conflict}.",
                "Zuerst das andere Rezept deinstallieren."));
        }

        foreach (var dependency in recipe.Dependencies.Where(d => !ledger.IsInstalled(d)))
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{recipe.Id} setzt {dependency} voraus, das nicht installiert ist."));
        }
    }

    /// <summary>Prüft die beschafften Dateien. Ohne passende Prüfsumme gilt eine Datei als fehlend.</summary>
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
                issues.Add(new PreflightIssue(IssueSeverity.Fatal, "Unzulässiger Quelldateiname.", e.Message));
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
                    $"Prüfsumme stimmt nicht: {source.FileName}",
                    $"erwartet {source.Sha256}, gefunden {actual}"));
                missing.Add(source.FileName);
            }
        }

        if (missing.Count > 0)
        {
            issues.Add(new PreflightIssue(
                IssueSeverity.Fatal,
                $"{missing.Count} benötigte Datei(en) fehlen oder sind nicht verifiziert.",
                string.Join(", ", missing)));
        }

        return missing;
    }

    private static void CheckProcesses(List<PreflightIssue> issues)
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
                        $"{name} läuft gerade.",
                        "Das Spiel muss geschlossen sein, sonst sind die Dateien gesperrt."));
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
    /// Der Snapshot kostet so viel Platz wie die Dateien, die er sichert. Das muss
    /// vorher geprüft werden — mitten im Lauf keinen Platz mehr zu haben ist genau
    /// der Zustand, den die ganze Pipeline verhindern soll.
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
                // Nicht lesbar: dann eben nicht mitgerechnet.
            }
        }

        var payloadBytes = recipe.RequiredFiles.Sum(s => s.SizeBytes);
        var needed = snapshotBytes + payloadBytes;

        foreach (var (root, label) in new[]
                 {
                     (AppPaths.Root, "Snapshot-Verzeichnis"),
                     (context.GameRoot, "Spielverzeichnis"),
                 })
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
                if (drive.AvailableFreeSpace < needed)
                {
                    issues.Add(new PreflightIssue(
                        IssueSeverity.Fatal,
                        $"Zu wenig Platz auf {drive.Name} ({label}).",
                        $"benötigt etwa {needed / 1024 / 1024} MB, frei {drive.AvailableFreeSpace / 1024 / 1024} MB"));
                }
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            {
                issues.Add(new PreflightIssue(
                    IssueSeverity.Warning,
                    $"Freier Speicher für {label} nicht ermittelbar.",
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
            return Fail(context, ["Der Plan enthält Blocker und wurde nicht ausgeführt."], null, false);
        }

        if (context.DryRun)
        {
            return Fail(context, ["Dry-Run: es wurde nichts ausgeführt."], null, false) with { Success = true };
        }

        // --- Phase 1: Pre-Flight, diesmal unmittelbar vor dem Schreiben -------
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
            log.Info($"Sichere {plan.AffectedPaths.Count} Pfad(e) vor der Änderung.");
            snapshot = snapshots.Create(plan.AffectedPaths);
            log.Info($"Snapshot {snapshot.Id} angelegt.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(context, [$"Snapshot fehlgeschlagen: {e.Message}"], null, false);
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
                errors.Add($"Schritt {applied + 1} ({planned.Description}) fehlgeschlagen: {e.Message}");
                break;
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
                    errors.Add($"Prüfung von '{planned.Description}' fehlgeschlagen: {e.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            log.Error($"{errors.Count} Fehler — Rollback wird ausgeführt.");
            var failures = snapshots.Restore(snapshot);

            foreach (var failure in failures)
            {
                log.Error($"Rollback unvollständig: {failure}");
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
                // Die Version wird neu ausgelesen, nicht aus dem Rezept uebernommen:
                // was tatsaechlich im Verzeichnis liegt, ist die Wahrheit.
                GameVersionAfter: ReadGameVersion(context.GameRoot))));

            log.Info($"{plan.Recipe.Id} eingetragen ({owned.Length} Datei(en)).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Die Änderung steht, aber wir können sie nicht buchführen. Das ist ein
            // Zustand, den wir nicht stehen lassen dürfen: ohne Ledger gibt es
            // später keinen Weg zurück.
            log.Error($"Ledger nicht schreibbar: {e.Message} — Rollback.");
            var failures = snapshots.Restore(snapshot);
            errors.Add($"Ledger nicht schreibbar: {e.Message}");
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
    /// Stellt fest, ob wir überhaupt ins Spielverzeichnis schreiben dürfen.
    ///
    /// Bewusst ohne Schreibprobe: der Plan darf nichts verändern, auch keine
    /// Testdatei. Stattdessen die beiden Tatsachen, die den Fall ausmachen —
    /// geschützter Pfad und fehlende erhöhte Rechte. Ohne diese Prüfung würde
    /// apply mitten im Entpacken scheitern und zurückrollen: das funktioniert,
    /// ist aber die unnötig teure Art, es herauszufinden.
    /// </summary>
    private static void CheckWritable(RecipeContext context, List<PreflightIssue> issues)
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
            "Das Spiel liegt in einem geschützten Verzeichnis, der Launcher läuft ohne Administratorrechte.",
            "Schreiben würde mitten im Lauf scheitern. Den Launcher als Administrator starten."));
    }

    /// <summary>Liest die Spielversion aus der EXE. Null, wenn sie nicht lesbar ist.</summary>
    public static string? ReadGameVersion(string gameRoot)
    {
        try
        {
            var exe = Path.Combine(gameRoot, Detection.InstallInspector.ExecutableName);
            if (!File.Exists(exe))
            {
                return null;
            }

            // Kanonisch ablegen: die Rohform schwankt je nach Binary zwischen
            // "1.0.7.0" und "1, 0, 7, 0".
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
