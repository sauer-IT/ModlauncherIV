using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Planning;

/// <summary>Why a recipe is on the path.</summary>
public enum JourneyReason
{
    /// <summary>The user picked it.</summary>
    Requested,

    /// <summary>It brings the game to the wanted version.</summary>
    VersionTransition,

    /// <summary>Another recipe requires it.</summary>
    Dependency,
}

public enum JourneyStepState
{
    /// <summary>Still has to run.</summary>
    Pending,

    /// <summary>Already in the ledger, same release. Gets skipped.</summary>
    AlreadyInstalled,

    /// <summary>
    /// In the ledger, but at a different release than the catalog has.
    ///
    /// Has to run. Without this distinction every user would be stuck on the
    /// release they first installed — the wizard would consider an outdated
    /// recipe finished and report "there is nothing to do".
    /// </summary>
    NeedsUpdate,
}

/// <param name="AtVersion">The game version in place at the time of this step.</param>
/// <param name="InstalledVersion">The release per the ledger, if already installed.</param>
public sealed record JourneyStep(
    Recipe Recipe,
    JourneyReason Reason,
    JourneyStepState State,
    string AtVersion,
    string? InstalledVersion = null);

/// <param name="Message">What makes the path impossible, in the user's language.</param>
public sealed record JourneyProblem(string Message, string? Detail = null);

/// <summary>
/// A planned path from the installation as found to the wanted state.
/// </summary>
public sealed record Journey(
    string FromVersion,
    string TargetVersion,
    IReadOnlyList<JourneyStep> Steps,
    IReadOnlyList<JourneyProblem> Problems)
{
    public bool IsPossible => Problems.Count == 0;

    /// <summary>The steps that actually still have to run.</summary>
    public IReadOnlyList<JourneyStep> Remaining =>
        Steps.Where(s => s.State != JourneyStepState.AlreadyInstalled).ToArray();

    public bool IsComplete => IsPossible && Remaining.Count == 0;
}

/// <param name="TargetVersion">Wanted game version. Null = keep the one in place.</param>
/// <param name="WantedRecipeIds">The recipes the user ticked.</param>
public sealed record JourneyRequest(
    string? TargetVersion,
    IReadOnlyList<string> WantedRecipeIds);

/// <summary>
/// Decides what has to happen, and in what order.
///
/// This is the difference between the CLI and a wizard: the CLI runs the recipe
/// you name. The wizard is supposed to work out by itself that "I want the
/// trainer" implies a downgrade, an ASI loader and a runtime first — and that
/// three of those are already installed.
///
/// The logic lives here and not in the window on purpose. A window cannot be
/// tested against fixtures, this class can; and this is exactly where the bugs
/// live that a user experiences as "the launcher wrecked my game".
/// </summary>
public static class JourneyPlanner
{
    public static Journey Plan(
        JourneyRequest request,
        IReadOnlyList<Recipe> catalog,
        string currentVersion,
        InstallLedger ledger,
        GameTitle game = GameTitle.GtaIV)
    {
        var problems = new List<JourneyProblem>();
        var steps = new List<JourneyStep>();

        var fromInfo = KnownVersions.Resolve(currentVersion);
        var from = fromInfo.Raw;
        var target = string.IsNullOrWhiteSpace(request.TargetVersion)
            ? from
            : KnownVersions.Normalise(request.TargetVersion);

        var byId = BuildIndex(catalog, game, problems);

        // Without a known starting version there is neither a path to search nor
        // a way to check suitability. Say it once, before it turns into a dozen
        // follow-up messages — but only when there is something to plan at all.
        // Someone who wants nothing needs no version either.
        var hasWork = request.WantedRecipeIds.Count > 0 || !string.Equals(from, target, StringComparison.OrdinalIgnoreCase);

        if (!fromInfo.IsKnown && hasWork)
        {
            problems.Add(new JourneyProblem(
                "The installed game version cannot be determined.",
                "Without a starting point there is no way to search a path or to check "
                + "whether a recipe fits. Give the version explicitly, or start the game "
                + "once unmodified."));
        }

        // ---------------------------------------------------------- Version path
        //
        // The version change always goes first. A downgrade swaps hundreds of
        // files; anything installed before it would afterwards be overwritten —
        // or, worse, half overwritten.

        var versionSteps = PlanVersionPath(catalog, game, from, fromInfo.IsKnown, target, problems);
        steps.AddRange(versionSteps);

        // From here on we reckon with the version the game has AFTER the change.
        // This is the point where a wizard differs from a command line: a recipe
        // for 1.0.7.0 is not unsuitable for a user on 1.2.0.59 — it is simply not
        // its turn yet.
        var effectiveVersion = versionSteps.Count > 0 ? target : from;

        // Whether suitability is checkable at all. If it is not, the reason is
        // already its own finding — reporting every recipe separately as "does
        // not fit (no version information)" would bury the one message that
        // matters under a pile of consequences.
        var versionIsUsable = versionSteps.Count > 0 || fromInfo.IsKnown;

        // ------------------------------------------------------------- Recipes

        var ordered = Resolve(request.WantedRecipeIds, byId, ledger, problems);

        foreach (var (recipe, reason) in ordered)
        {
            if (versionIsUsable && !recipe.Matches(effectiveVersion))
            {
                problems.Add(new JourneyProblem(
                    $"{recipe.Name} does not fit version {effectiveVersion}.",
                    $"Intended for: {string.Join(", ", recipe.AppliesTo)}"));

                continue;
            }

            // Installed does not mean finished: if the catalog holds a different
            // release than the ledger, the recipe has to run. The ledger still
            // keeps exactly one entry, and the snapshot taken beforehand
            // preserves the files of the old release.
            var installed = ledger.Find(recipe.Id);

            var state = installed switch
            {
                null => JourneyStepState.Pending,
                _ when string.Equals(installed.RecipeVersion, recipe.Version, StringComparison.OrdinalIgnoreCase)
                    => JourneyStepState.AlreadyInstalled,
                _ => JourneyStepState.NeedsUpdate,
            };

            steps.Add(new JourneyStep(recipe, reason, state, effectiveVersion, installed?.RecipeVersion));
        }

        CheckConflicts(steps, ledger, problems);

        return new Journey(from, target, steps, problems);
    }

    // ------------------------------------------------------------------ Helpers

    private static Dictionary<string, Recipe> BuildIndex(
        IReadOnlyList<Recipe> catalog,
        GameTitle game,
        List<JourneyProblem> problems)
    {
        var byId = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in catalog.Where(r => r.Game == game))
        {
            // Two recipes sharing an id is not an edge case, it is a catalog you
            // cannot trust: otherwise the order of the file system decides which
            // of the two was meant.
            if (!byId.TryAdd(recipe.Id, recipe))
            {
                problems.Add(new JourneyProblem(
                    $"The recipe id {recipe.Id} appears more than once in the catalog."));
            }
        }

        return byId;
    }

    private static List<JourneyStep> PlanVersionPath(
        IReadOnlyList<Recipe> catalog,
        GameTitle game,
        string from,
        bool fromIsKnown,
        string target,
        List<JourneyProblem> problems)
    {
        if (string.Equals(from, target, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        // The unknown starting version is already reported above; here it would
        // only be a second message for the same cause.
        if (!fromIsKnown)
        {
            return [];
        }

        var path = VersionGraph.Build(catalog, game).FindPath(from, target);

        if (path is null)
        {
            problems.Add(new JourneyProblem(
                $"No known path leads from {from} to {target}.",
                "The catalog is missing a recipe for this version change."));

            return [];
        }

        // The starting node travels along: after the first edge the game sits on
        // that edge's target version, and the next edge starts from there.
        return path
            .Select(edge => new JourneyStep(
                edge.Recipe,
                JourneyReason.VersionTransition,
                JourneyStepState.Pending,
                edge.From))
            .ToList();
    }

    /// <summary>
    /// Resolves dependencies and orders the recipes so that each one comes after
    /// everything it needs.
    ///
    /// Depth-first rather than Kahn's algorithm, because it hands back the cycle:
    /// "a needs b needs a" is useful to a user, "2 recipes left over" is not.
    /// </summary>
    private static List<(Recipe Recipe, JourneyReason Reason)> Resolve(
        IReadOnlyList<string> wanted,
        Dictionary<string, Recipe> byId,
        InstallLedger ledger,
        List<JourneyProblem> problems)
    {
        var result = new List<(Recipe, JourneyReason)>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onPath = new List<string>();

        void Visit(string id, JourneyReason reason)
        {
            if (done.Contains(id))
            {
                return;
            }

            var cycleAt = onPath.FindIndex(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
            if (cycleAt >= 0)
            {
                problems.Add(new JourneyProblem(
                    "Two recipes require one another.",
                    string.Join(" -> ", onPath.Skip(cycleAt).Append(id))));

                return;
            }

            if (!byId.TryGetValue(id, out var recipe))
            {
                // An already installed recipe may vanish from the catalog without
                // everything grinding to a halt — its files are still there.
                if (ledger.IsInstalled(id))
                {
                    done.Add(id);
                    return;
                }

                problems.Add(new JourneyProblem(
                    $"The recipe {id} is not in the catalog.",
                    reason == JourneyReason.Dependency
                        ? "Another recipe requires it."
                        : null));

                return;
            }

            onPath.Add(id);

            foreach (var dependency in recipe.Dependencies)
            {
                Visit(dependency, JourneyReason.Dependency);
            }

            onPath.RemoveAt(onPath.Count - 1);

            done.Add(id);
            result.Add((recipe, reason));
        }

        foreach (var id in wanted)
        {
            Visit(id, JourneyReason.Requested);
        }

        return result;
    }

    /// <summary>
    /// Checks <c>ConflictsWith</c> against the path itself and against what is
    /// already installed. Two ASI loaders side by side is not a problem you want
    /// to notice only after writing files.
    /// </summary>
    private static void CheckConflicts(
        List<JourneyStep> steps,
        InstallLedger ledger,
        List<JourneyProblem> problems)
    {
        // Id -> readable name. A user did not pick "ultimate-asi-loader", they
        // picked "Ultimate ASI Loader"; a message mixing both spellings reads
        // like a half-translated error.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ledger.Entries)
        {
            names[entry.RecipeId] = entry.RecipeName;
        }

        foreach (var step in steps)
        {
            names[step.Recipe.Id] = step.Recipe.Name;
        }

        var present = new HashSet<string>(names.Keys, StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            foreach (var other in step.Recipe.Conflicts.Where(present.Contains))
            {
                // Conflicts are meant mutually but are often only declared on one
                // side. Report the pair once, not twice.
                var pair = string.CompareOrdinal(step.Recipe.Id, other) < 0
                    ? $"{step.Recipe.Id}|{other}"
                    : $"{other}|{step.Recipe.Id}";

                if (reported.Add(pair))
                {
                    problems.Add(new JourneyProblem(
                        $"{step.Recipe.Name} conflicts with {names[other]}.",
                        "Both want to occupy the same place in the game. Pick only one of them."));
                }
            }
        }
    }
}
