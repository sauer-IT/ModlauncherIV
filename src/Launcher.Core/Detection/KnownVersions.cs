namespace ModlauncherIV.Core.Detection;

/// <summary>
/// The nodes of the version graph. The edges are the downgrade recipes.
///
/// IMPORTANT: the Complete Edition builds below 1.2.0.59 are not verified. They
/// are listed so detection does not reject them as "unknown" — but before a
/// recipe is built against any of them, they need to be confirmed.
/// </summary>
public static class KnownVersions
{
    private static readonly GameVersionInfo[] All =
    [
        // --- Complete Edition (2020+): no GFWL, no multiplayer, radio songs cut ---
        Ce("1.2.0.59", "Complete Edition"),
        Ce("1.2.0.43", "Complete Edition, older"),
        Ce("1.2.0.32", "Complete Edition, older"),
        Ce("1.2.0.30", "Complete Edition, older"),

        // --- The classic builds ---
        // A target since the FusionFix chain turned out to be 1.0.8.0 only. It
        // was listed as "not for modding" while four recipes in the catalog
        // wanted nothing else, which left those four permanently out of reach.
        Classic("1.0.8.0", "Patch 8 — last build before the Complete Edition, and what FusionFix needs", moddingTarget: true),
        Classic("1.0.7.0", "Patch 7 — the standard target for modding", moddingTarget: true),
        Classic("1.0.6.0", "Patch 6", moddingTarget: false),
        Classic("1.0.4.0", "Patch 4 — the alternative modding target", moddingTarget: true),
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

    /// <summary>Maps a raw version string onto a known node.</summary>
    public static GameVersionInfo Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GameVersionInfo.Unrecognised("(no version information)");
        }

        var normalised = raw.Trim();

        var exact = All.FirstOrDefault(v =>
            string.Equals(v.Raw, normalised, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // FileVersion can read "1, 2, 0, 59" or "1.2.0.59 (build …)".
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

    /// <summary>
    /// Brings a version string into its canonical spelling.
    ///
    /// Necessary because FileVersionInfo reports either "1.0.7.0" or
    /// "1, 0, 7, 0" depending on the binary. Comparing both forms unexamined
    /// reports a difference where there is none — and that devalues exactly the
    /// warning you have to rely on when the store patches the game back.
    /// </summary>
    public static string Normalise(string? raw) => Resolve(raw).Raw;

    /// <summary>Every version the game can be downgraded to.</summary>
    public static IReadOnlyList<GameVersionInfo> ModdingTargets =>
        All.Where(v => v.IsModdingTarget).ToArray();
}
