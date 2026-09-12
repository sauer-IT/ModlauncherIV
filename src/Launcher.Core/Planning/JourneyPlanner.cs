using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Planning;

/// <summary>Warum ein Rezept im Weg steht.</summary>
public enum JourneyReason
{
    /// <summary>Der Nutzer hat es ausgewählt.</summary>
    Requested,

    /// <summary>Es bringt das Spiel auf die gewünschte Version.</summary>
    VersionTransition,

    /// <summary>Ein anderes Rezept verlangt es.</summary>
    Dependency,
}

public enum JourneyStepState
{
    /// <summary>Muss noch laufen.</summary>
    Pending,

    /// <summary>Steht schon im Ledger. Wird übersprungen.</summary>
    AlreadyInstalled,
}

/// <param name="AtVersion">Spielversion, die zum Zeitpunkt dieses Schritts vorliegt.</param>
public sealed record JourneyStep(
    Recipe Recipe,
    JourneyReason Reason,
    JourneyStepState State,
    string AtVersion);

/// <param name="Message">Was den Weg unmöglich macht, in der Sprache des Nutzers.</param>
public sealed record JourneyProblem(string Message, string? Detail = null);

/// <summary>
/// Ein geplanter Weg von der vorgefundenen Installation zum gewünschten Zustand.
/// </summary>
public sealed record Journey(
    string FromVersion,
    string TargetVersion,
    IReadOnlyList<JourneyStep> Steps,
    IReadOnlyList<JourneyProblem> Problems)
{
    public bool IsPossible => Problems.Count == 0;

    /// <summary>Die Schritte, die tatsächlich noch laufen müssen.</summary>
    public IReadOnlyList<JourneyStep> Remaining =>
        Steps.Where(s => s.State == JourneyStepState.Pending).ToArray();

    public bool IsComplete => IsPossible && Remaining.Count == 0;
}

/// <param name="TargetVersion">Gewünschte Spielversion. Null = die vorhandene behalten.</param>
/// <param name="WantedRecipeIds">Rezepte, die der Nutzer angehakt hat.</param>
public sealed record JourneyRequest(
    string? TargetVersion,
    IReadOnlyList<string> WantedRecipeIds);

/// <summary>
/// Entscheidet, was in welcher Reihenfolge zu tun ist.
///
/// Das ist der Unterschied zwischen der CLI und einem Assistenten: die CLI führt
/// ein Rezept aus, das man ihr nennt. Der Assistent soll aus "ich will den Trainer"
/// selbst ableiten, dass davor ein Downgrade, ein ASI-Loader und eine Laufzeit
/// stehen — und dass drei davon schon installiert sind.
///
/// Die Logik liegt bewusst hier und nicht im Fenster. Ein Fenster lässt sich nicht
/// gegen Fixtures testen, diese Klasse schon; und genau hier liegen die Fehler, die
/// ein Nutzer als "der Launcher hat mir das Spiel zerlegt" erlebt.
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

        // Ohne bekannte Ausgangsversion gibt es weder einen Weg noch eine
        // Eignungspruefung. Das einmal sagen, und zwar bevor daraus ein Dutzend
        // Folgemeldungen werden - aber nur, wenn ueberhaupt etwas geplant werden
        // soll. Wer nichts will, braucht auch keine Version.
        var hasWork = request.WantedRecipeIds.Count > 0 || !string.Equals(from, target, StringComparison.OrdinalIgnoreCase);

        if (!fromInfo.IsKnown && hasWork)
        {
            problems.Add(new JourneyProblem(
                "Die vorhandene Spielversion lässt sich nicht bestimmen.",
                "Ohne Ausgangspunkt lässt sich weder ein Weg suchen noch prüfen, ob ein "
                + "Rezept passt. Version vorgeben oder das Spiel einmal unverändert starten."));
        }

        // ---------------------------------------------------------- Versionsweg
        //
        // Der Versionswechsel steht immer vorn. Ein Downgrade tauscht hunderte
        // Dateien aus; alles, was vorher hineingelegt wurde, wäre danach
        // überschrieben oder — schlimmer — halb überschrieben.

        var versionSteps = PlanVersionPath(catalog, game, from, fromInfo.IsKnown, target, problems);
        steps.AddRange(versionSteps);

        // Ab hier rechnen wir mit der Version, die das Spiel nach dem Wechsel hat.
        // Das ist der Punkt, an dem ein Assistent sich von einer Befehlszeile
        // unterscheidet: ein Rezept für 1.0.7.0 ist für einen Nutzer auf 1.2.0.59
        // nicht etwa ungeeignet, sondern schlicht noch nicht an der Reihe.
        var effectiveVersion = versionSteps.Count > 0 ? target : from;

        // Ob die Eignung ueberhaupt pruefbar ist. Ist sie es nicht, steht der
        // Grund schon als eigener Befund da - dann jedes Rezept einzeln als
        // "passt nicht zu (keine Versionsinformation)" zu melden, vergraebt die
        // eine Meldung, auf die es ankommt, unter lauter Folgemeldungen.
        var versionIsUsable = versionSteps.Count > 0 || fromInfo.IsKnown;

        // ------------------------------------------------------------- Rezepte

        var ordered = Resolve(request.WantedRecipeIds, byId, ledger, problems);

        foreach (var (recipe, reason) in ordered)
        {
            if (versionIsUsable && !recipe.Matches(effectiveVersion))
            {
                problems.Add(new JourneyProblem(
                    $"{recipe.Name} passt nicht zu Version {effectiveVersion}.",
                    $"Vorgesehen für: {string.Join(", ", recipe.AppliesTo)}"));

                continue;
            }

            steps.Add(new JourneyStep(
                recipe,
                reason,
                ledger.IsInstalled(recipe.Id) ? JourneyStepState.AlreadyInstalled : JourneyStepState.Pending,
                effectiveVersion));
        }

        CheckConflicts(steps, ledger, problems);

        return new Journey(from, target, steps, problems);
    }

    // ------------------------------------------------------------------ Helfer

    private static Dictionary<string, Recipe> BuildIndex(
        IReadOnlyList<Recipe> catalog,
        GameTitle game,
        List<JourneyProblem> problems)
    {
        var byId = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in catalog.Where(r => r.Game == game))
        {
            // Zwei Rezepte mit derselben ID sind kein Randfall, sondern ein Katalog,
            // dem man nicht trauen kann: welches von beiden gemeint ist, entscheidet
            // sonst die Reihenfolge im Dateisystem.
            if (!byId.TryAdd(recipe.Id, recipe))
            {
                problems.Add(new JourneyProblem(
                    $"Die Rezept-ID {recipe.Id} kommt im Katalog mehrfach vor."));
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

        // Die unbekannte Ausgangsversion ist oben schon gemeldet; hier bliebe
        // nur eine zweite Meldung fuer dieselbe Ursache.
        if (!fromIsKnown)
        {
            return [];
        }

        var path = VersionGraph.Build(catalog, game).FindPath(from, target);

        if (path is null)
        {
            problems.Add(new JourneyProblem(
                $"Von {from} führt kein bekannter Weg auf {target}.",
                "Im Katalog fehlt ein Rezept für diesen Versionswechsel."));

            return [];
        }

        // Der Ausgangsknoten wandert mit: nach der ersten Kante steht das Spiel
        // auf deren Zielversion, und die nächste Kante setzt dort an.
        return path
            .Select(edge => new JourneyStep(
                edge.Recipe,
                JourneyReason.VersionTransition,
                JourneyStepState.Pending,
                edge.From))
            .ToList();
    }

    /// <summary>
    /// Löst Abhängigkeiten auf und bringt die Rezepte in eine Reihenfolge, in der
    /// jedes nach allem steht, was es braucht.
    ///
    /// Tiefensuche statt Kahn-Algorithmus, weil sie den Zyklus mitliefert: "a
    /// braucht b braucht a" ist für den Nutzer brauchbar, "es bleiben 2 Rezepte
    /// übrig" nicht.
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
                    "Zwei Rezepte verlangen einander gegenseitig.",
                    string.Join(" -> ", onPath.Skip(cycleAt).Append(id))));

                return;
            }

            if (!byId.TryGetValue(id, out var recipe))
            {
                // Ein bereits installiertes Rezept darf aus dem Katalog verschwinden,
                // ohne dass deshalb nichts mehr geht — die Dateien liegen ja da.
                if (ledger.IsInstalled(id))
                {
                    done.Add(id);
                    return;
                }

                problems.Add(new JourneyProblem(
                    $"Das Rezept {id} steht nicht im Katalog.",
                    reason == JourneyReason.Dependency
                        ? "Es wird von einem anderen Rezept verlangt."
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
    /// Prüft <c>ConflictsWith</c> gegen den Weg selbst und gegen das, was schon
    /// installiert ist. Zwei ASI-Loader nebeneinander sind kein Fehler, den man
    /// erst nach dem Schreiben bemerken will.
    /// </summary>
    private static void CheckConflicts(
        List<JourneyStep> steps,
        InstallLedger ledger,
        List<JourneyProblem> problems)
    {
        // ID -> Klartextname. Ein Nutzer hat "ultimate-asi-loader" nicht
        // ausgewaehlt, sondern "Ultimate ASI Loader"; eine Meldung, die beide
        // Schreibweisen mischt, liest sich wie ein halb uebersetzter Fehler.
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
                // Konflikte sind gegenseitig gemeint, stehen aber oft nur auf einer
                // Seite. Das Paar einmal melden, nicht zweimal.
                var pair = string.CompareOrdinal(step.Recipe.Id, other) < 0
                    ? $"{step.Recipe.Id}|{other}"
                    : $"{other}|{step.Recipe.Id}";

                if (reported.Add(pair))
                {
                    problems.Add(new JourneyProblem(
                        $"{step.Recipe.Name} verträgt sich nicht mit {names[other]}.",
                        "Beide wollen dieselbe Stelle im Spiel besetzen. Nur eines von beiden auswählen."));
                }
            }
        }
    }
}
