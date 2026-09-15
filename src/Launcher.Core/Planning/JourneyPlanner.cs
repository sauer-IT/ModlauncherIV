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

    /// <summary>
    /// Installed, and taken back from its snapshot before anything else runs.
    ///
    /// Two kinds are. A downgrade, because the two downgrades both start from
    /// the Complete Edition and the way from one to the other leads back through
    /// the original files. And a mod made for another version than the one the
    /// journey ends on, because left in place it loads nothing - or, like
    /// FusionFix on 1.0.7.0, keeps the game from starting. Nothing is installed
    /// by this step and nothing is fetched for it.
    /// </summary>
    TakeBack,
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

    /// <summary>The open steps that install something - the ones that need files.</summary>
    public IReadOnlyList<JourneyStep> ToInstall =>
        Remaining.Where(s => s.Reason != JourneyReason.TakeBack).ToArray();

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

        var versionSteps = PlanVersionPath(catalog, game, from, fromInfo.IsKnown, target, ledger, problems);
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

            // An update is a later release, not merely a different one. Taking
            // "different" for "newer" installed an older trainer over a newer
            // one. What nothing can put in order still runs, as it always did:
            // the catalog changed it, and there is no way to know better.
            var state = installed is null
                ? JourneyStepState.Pending
                : ReleaseVersion.Compare(installed.RecipeVersion, recipe.Version) switch
                {
                    ReleaseOrder.Newer or ReleaseOrder.Unordered => JourneyStepState.NeedsUpdate,
                    _ => JourneyStepState.AlreadyInstalled,
                };

            steps.Add(new JourneyStep(recipe, reason, state, effectiveVersion, installed?.RecipeVersion));
        }

        // What will not work on the version this ends on comes out first -
        // before the version change, which may itself be a take-back.
        steps.InsertRange(0, PlanTakeBacks(steps, byId, ledger, from, target));

        CheckConflicts(steps, ledger, problems);

        return new Journey(from, target, steps, problems);
    }

    /// <summary>
    /// Takes out what would no longer work on the version this ends on.
    ///
    /// Recipes are checked against the version when they are installed, so
    /// everything in the ledger fitted the game as it was. Move the game
    /// underneath them and some no longer do - and their files are all still
    /// exactly where they were put. This used to be reported and left alone,
    /// on the grounds that taking mods out was not the planner's decision.
    /// Found in the game what that costs: 1.0.8.0 to 1.0.7.0, FusionFix and
    /// three packs built on it stayed in, and the game would not start until
    /// they were removed by hand, one by one, on the home page.
    ///
    /// So they are taken back from their snapshots, newest first: a pack that
    /// needs FusionFix goes before FusionFix, which is also the only order the
    /// uninstaller accepts. Whatever still needs one of them goes with it - it
    /// could not work without it. Their files stay in the cache, so switching
    /// back and installing them again fetches nothing.
    ///
    /// Without a version change this finds whatever is already installed and
    /// does not fit - the state the home page warns about - and the same plan
    /// cleans it up.
    /// </summary>
    private static List<JourneyStep> PlanTakeBacks(
        List<JourneyStep> steps,
        Dictionary<string, Recipe> byId,
        InstallLedger ledger,
        string from,
        string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return [];
        }

        var planned = steps.Select(s => s.Recipe.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ledger.Entries)
        {
            if (planned.Contains(entry.RecipeId) || !byId.TryGetValue(entry.RecipeId, out var recipe))
            {
                continue;
            }

            // A recipe that names no versions fits all of them, and a downgrade
            // is the version path's business, not this one's.
            if (recipe.AppliesTo.Count == 0 || recipe.IsVersionTransition || recipe.Matches(target))
            {
                continue;
            }

            leaving.Add(recipe.Id);
        }

        // Whatever needs something that is leaving cannot stay either.
        bool grew;

        do
        {
            grew = false;

            foreach (var entry in ledger.Entries)
            {
                if (leaving.Contains(entry.RecipeId)
                    || planned.Contains(entry.RecipeId)
                    || !byId.TryGetValue(entry.RecipeId, out var recipe)
                    || recipe.IsVersionTransition
                    || !recipe.Dependencies.Any(leaving.Contains))
                {
                    continue;
                }

                leaving.Add(recipe.Id);
                grew = true;
            }
        }
        while (grew);

        return ledger.Entries
            .Where(e => leaving.Contains(e.RecipeId))
            .OrderByDescending(e => e.InstalledAt)
            .Select(e => new JourneyStep(byId[e.RecipeId], JourneyReason.TakeBack, JourneyStepState.Pending, from, e.RecipeVersion))
            .ToList();
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
        InstallLedger ledger,
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

        var graph = VersionGraph.Build(catalog, game);
        var path = graph.FindPath(from, target);

        if (path is not null)
        {
            return Edges(path);
        }

        // No edge leads there from here - but the game may only be here because
        // a downgrade put it here, and that downgrade's snapshot is a way back.
        // 1.0.7.0 to 1.0.8.0 is exactly this: both downgrades start from the
        // Complete Edition, and there is no edge between them and should not be
        // one. Taking the first back and running the second used to be two
        // trips, one of them to the home page to press Remove.
        var back = catalog.FirstOrDefault(r =>
            r.Game == game
            && r.IsVersionTransition
            && string.Equals(r.ProducesVersion, from, StringComparison.OrdinalIgnoreCase)
            && ledger.IsInstalled(r.Id));

        if (back is not null)
        {
            // Which of the versions it applies to it was installed over is not
            // written down. It does not have to be: after the take-back the game
            // is read again, and the recipes that follow are checked against
            // what is actually there. Here it is enough that one of them leads on.
            foreach (var original in back.AppliesTo)
            {
                var onward = string.Equals(original, target, StringComparison.OrdinalIgnoreCase)
                    ? []
                    : graph.FindPath(original, target);

                if (onward is null)
                {
                    continue;
                }

                var steps = new List<JourneyStep>
                {
                    new(back, JourneyReason.TakeBack, JourneyStepState.Pending, from, ledger.Find(back.Id)?.RecipeVersion),
                };

                steps.AddRange(Edges(onward));
                return steps;
            }
        }

        problems.Add(new JourneyProblem(
            $"No known path leads from {from} to {target}.",
            "The catalog is missing a recipe for this version change."));

        return [];
    }

    // The starting node travels along: after the first edge the game sits on
    // that edge's target version, and the next edge starts from there.
    private static List<JourneyStep> Edges(IReadOnlyList<VersionEdge> path) =>
        path
            .Select(edge => new JourneyStep(
                edge.Recipe,
                JourneyReason.VersionTransition,
                JourneyStepState.Pending,
                edge.From))
            .ToList();

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

        // What is being taken back is on its way out, not in the way.
        var leaving = steps.Where(s => s.Reason == JourneyReason.TakeBack).Select(s => s.Recipe.Id);

        var present = new HashSet<string>(names.Keys, StringComparer.OrdinalIgnoreCase);
        present.ExceptWith(leaving);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps.Where(s => s.Reason != JourneyReason.TakeBack))
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
