using System.Security.Cryptography;

namespace ModlauncherIV.Core;

/// <summary>
/// SHA-256 an einer Stelle. Jede Prüfsumme im Projekt ist kleingeschriebenes Hex,
/// damit Vergleiche nie an der Schreibweise scheitern.
/// </summary>
public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Vergleicht zwei Prüfsummen unabhängig von Schreibweise und Leerzeichen.</summary>
    public static bool Equal(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) &&
        !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
