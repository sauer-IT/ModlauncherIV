using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Catalog;

/// <summary>An edge: a recipe that takes the game from one version to another.</summary>
public sealed record VersionEdge(string From, string To, Recipe Recipe);

/// <summary>
/// The version graph.
///
/// Versions are nodes, version-changing recipes are edges. That makes a
/// downgrade no special case in the code but a path search: 1.0.4.0 is simply
/// one more edge, and a new patch level one more catalog entry.
///
/// The search looks for the path with the fewest steps. Every step is a full
/// recipe run with its own snapshot — fewer steps means fewer chances to fail
/// along the way.
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
            // Without AppliesToVersions we would not know where the edge starts —
            // a recipe leading "from anywhere" to a version would be a claim
            // nobody ever checked.
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
    /// Shortest path from <paramref name="from"/> to <paramref name="to"/>, as a
    /// sequence of recipes. Null when there is none.
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

    /// <summary>Every version reachable from here.</summary>
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
