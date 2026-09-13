namespace ModlauncherIV.Core.Catalog;

/// <summary>How a release in the catalog stands to the one that is installed.</summary>
public enum ReleaseOrder
{
    /// <summary>Exactly the same release.</summary>
    Same,

    /// <summary>The catalog has a later release.</summary>
    Newer,

    /// <summary>The catalog has an earlier release than the one installed.</summary>
    Older,

    /// <summary>
    /// Same release number, different build - a rebuild of the same thing.
    /// Neither newer nor older, and not worth offering.
    /// </summary>
    SameReleaseDifferentBuild,

    /// <summary>They differ, and nothing in either says which comes first.</summary>
    Unordered,
}

/// <summary>
/// Puts two release strings in order.
///
/// Before this, releases were only ever compared for equality, and "not equal"
/// was taken to mean "newer". The home page said so, and its Update button acted
/// on it - which installed an older trainer over a newer one the moment the
/// launcher on disk was older than the build in the game. Measured, not
/// imagined: 04cbb02b in the game, 0e3aca81 inside the launcher, one click, and
/// the fix that build carried was gone.
///
/// The rules are semantic versioning's, as far as they are needed here: numbers
/// compared part by part, a pre-release below its release, and build metadata
/// after the "+" ignored for order - two builds of the same release are the same
/// release. Anything that does not parse is compared as text, and when that
/// text differs the honest answer is <see cref="ReleaseOrder.Unordered"/>.
/// </summary>
public static class ReleaseVersion
{
    /// <summary>Where <paramref name="candidate"/> stands relative to <paramref name="installed"/>.</summary>
    public static ReleaseOrder Compare(string? installed, string? candidate)
    {
        var a = (installed ?? string.Empty).Trim();
        var b = (candidate ?? string.Empty).Trim();

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseOrder.Same;
        }

        var (coreA, preA, buildA) = Split(a);
        var (coreB, preB, buildB) = Split(b);

        var numbersA = Numbers(coreA);
        var numbersB = Numbers(coreB);

        if (numbersA is null || numbersB is null)
        {
            return ReleaseOrder.Unordered;
        }

        var parts = Math.Max(numbersA.Length, numbersB.Length);

        for (var i = 0; i < parts; i++)
        {
            var x = i < numbersA.Length ? numbersA[i] : 0;
            var y = i < numbersB.Length ? numbersB[i] : 0;

            if (x != y)
            {
                return y > x ? ReleaseOrder.Newer : ReleaseOrder.Older;
            }
        }

        // Same numbers. A pre-release comes before the release it leads up to.
        if (!string.Equals(preA, preB, StringComparison.OrdinalIgnoreCase))
        {
            if (preA.Length == 0)
            {
                return ReleaseOrder.Older;
            }

            if (preB.Length == 0)
            {
                return ReleaseOrder.Newer;
            }

            return string.CompareOrdinal(preB, preA) > 0 ? ReleaseOrder.Newer : ReleaseOrder.Older;
        }

        return string.Equals(buildA, buildB, StringComparison.OrdinalIgnoreCase)
            ? ReleaseOrder.Same
            : ReleaseOrder.SameReleaseDifferentBuild;
    }

    private static (string Core, string Pre, string Build) Split(string version)
    {
        var plus = version.IndexOf('+');
        var build = plus < 0 ? string.Empty : version[(plus + 1)..];
        var rest = plus < 0 ? version : version[..plus];

        var dash = rest.IndexOf('-');
        var pre = dash < 0 ? string.Empty : rest[(dash + 1)..];
        var core = dash < 0 ? rest : rest[..dash];

        return (core, pre, build);
    }

    private static long[]? Numbers(string core)
    {
        if (core.Length == 0)
        {
            return null;
        }

        var parts = core.Split('.');
        var numbers = new long[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return null;
            }
        }

        return numbers;
    }
}
