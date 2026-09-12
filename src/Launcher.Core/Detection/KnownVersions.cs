namespace ModlauncherIV.Core.Detection;

/// <summary>
/// Die Knoten des Versionsgraphen. Kanten (= Downgrade-Rezepte) kommen in M3 dazu.
///
/// WICHTIG: Die Complete-Edition-Stände unterhalb von 1.2.0.59 sind noch nicht
/// verifiziert (siehe Abschnitt 10 des Projektplans). Sie stehen hier, damit die
/// Erkennung sie nicht als "unbekannt" abweist — bevor ein Rezept dagegen gebaut
/// wird, müssen sie bestätigt werden.
/// </summary>
public static class KnownVersions
{
    private static readonly GameVersionInfo[] All =
    [
        // --- Complete Edition (2020+): kein GFWL, kein Multiplayer, Radiosongs entfernt ---
        Ce("1.2.0.59", "Complete Edition"),
        Ce("1.2.0.43", "Complete Edition, älter"),
        Ce("1.2.0.32", "Complete Edition, älter"),
        Ce("1.2.0.30", "Complete Edition, älter"),

        // --- Klassische Stände ---
        Classic("1.0.8.0", "Patch 8 — letzter Stand vor der Complete Edition", moddingTarget: false),
        Classic("1.0.7.0", "Patch 7 — Standardziel fürs Modding", moddingTarget: true),
        Classic("1.0.6.0", "Patch 6", moddingTarget: false),
        Classic("1.0.4.0", "Patch 4 — alternatives Modding-Ziel", moddingTarget: true),
    ];

    private static GameVersionInfo Ce(string raw, string name) => new(
        Raw: raw,
        Parsed: Version.Parse(raw),
        DisplayName: name,
        IsCompleteEdition: true,
        RequiresGfwl: false,
        IsModdingTarget: false,
        IsKnown: true);

    private static GameVersionInfo Classic(string raw, string name, bool moddingTarget) => new(
        Raw: raw,
        Parsed: Version.Parse(raw),
        DisplayName: name,
        IsCompleteEdition: false,
        RequiresGfwl: true,
        IsModdingTarget: moddingTarget,
        IsKnown: true);

    /// <summary>Ordnet einen rohen Versionsstring einem bekannten Knoten zu.</summary>
    public static GameVersionInfo Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GameVersionInfo.Unrecognised("(keine Versionsinformation)");
        }

        var normalised = raw.Trim();

        var exact = All.FirstOrDefault(v =>
            string.Equals(v.Raw, normalised, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // FileVersion kann "1, 2, 0, 59" oder "1.2.0.59 (build …)" lauten.
        if (Version.TryParse(normalised.Replace(", ", ".").Replace(",", "."), out var parsed))
        {
            var byNumber = All.FirstOrDefault(v => v.Parsed == parsed);
            if (byNumber is not null)
            {
                return byNumber;
            }
        }

        return GameVersionInfo.Unrecognised(normalised);
    }

    /// <summary>Alle Versionen, auf die heruntergestuft werden kann.</summary>
    public static IReadOnlyList<GameVersionInfo> ModdingTargets =>
        All.Where(v => v.IsModdingTarget).ToArray();
}
