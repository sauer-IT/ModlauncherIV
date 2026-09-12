using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Catalog;

/// <summary>Eine Kante: ein Rezept, das das Spiel von einer Version auf eine andere bringt.</summary>
public sealed record VersionEdge(string From, string To, Recipe Recipe);

/// <summary>
/// Der Versionsgraph aus Abschnitt 04 des Plans.
///
/// Versionen sind Knoten, versionsändernde Rezepte sind Kanten. Damit ist ein
/// Downgrade keine Sonderbehandlung im Code, sondern eine Wegsuche: 1.0.4.0 ist
/// schlicht eine Kante mehr, und ein neuer Patchstand ein Eintrag im Katalog.
///
/// Gesucht wird der Weg mit den wenigsten Schritten. Jeder Schritt ist ein
/// vollständiger Rezeptlauf mit Snapshot — weniger Schritte heißt weniger
/// Gelegenheiten, unterwegs zu scheitern.
/// </summary>
public sealed class VersionGraph
{
    private readonly Dictionary<string, List<VersionEdge>> _edges;

    private VersionGraph(Dictionary<string, List<VersionEdge>> edges) => _edges = edges;

    public IReadOnlyList<VersionEdge> AllEdges => _edges.Values.SelectMany(e => e).ToArray();

    public static VersionGraph Build(IEnumerable<Recipe> recipes, GameTitle game = GameTitle.GtaIV)
    {
        var edges = new Dictionary<string, List<VersionEdge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in recipes.Where(r => r.Game == game && r.IsVersionTransition))
        {
            // Ohne AppliesToVersions wüssten wir nicht, wo die Kante beginnt —
            // ein Rezept, das "von überall" auf eine Version führt, wäre eine
            // Behauptung, die niemand geprüft hat.
            foreach (var from in recipe.AppliesTo)
            {
                if (!edges.TryGetValue(from, out var list))
                {
                    list = [];
                    edges[from] = list;
                }

                list.Add(new VersionEdge(from, recipe.ProducesVersion!, recipe));
            }
        }

        return new VersionGraph(edges);
    }

    /// <summary>
    /// Kürzester Weg von <paramref name="from"/> nach <paramref name="to"/>,
    /// als Folge von Rezepten. Null, wenn es keinen gibt.
    /// </summary>
    public IReadOnlyList<VersionEdge>? FindPath(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var queue = new Queue<string>();
        var cameFrom = new Dictionary<string, VersionEdge>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };

        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!_edges.TryGetValue(current, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (!seen.Add(edge.To))
                {
                    continue;
                }

                cameFrom[edge.To] = edge;

                if (string.Equals(edge.To, to, StringComparison.OrdinalIgnoreCase))
                {
                    return Reconstruct(cameFrom, from, edge.To);
                }

                queue.Enqueue(edge.To);
            }
        }

        return null;
    }

    /// <summary>Alle Versionen, die von hier aus erreichbar sind.</summary>
    public IReadOnlyList<string> ReachableFrom(string version)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(version);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_edges.TryGetValue(current, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing.Where(e => seen.Add(e.To)))
            {
                queue.Enqueue(edge.To);
            }
        }

        return seen.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<VersionEdge> Reconstruct(
        Dictionary<string, VersionEdge> cameFrom,
        string start,
        string end)
    {
        var path = new List<VersionEdge>();
        var current = end;

        while (!string.Equals(current, start, StringComparison.OrdinalIgnoreCase))
        {
            var edge = cameFrom[current];
            path.Add(edge);
            current = edge.From;
        }

        path.Reverse();
        return path;
    }
}
