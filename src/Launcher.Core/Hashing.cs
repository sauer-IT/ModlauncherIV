using System.Security.Cryptography;

namespace ModlauncherIV.Core;

/// <summary>
/// SHA-256 in one place. Every checksum in this project is lower-case hex, so
/// that comparisons never fail over spelling.
/// </summary>
public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Compares two checksums regardless of case and surrounding spaces.</summary>
    public static bool Equal(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) &&
        !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
