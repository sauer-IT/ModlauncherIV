using System.Security.Cryptography;
using System.Text.Json;

namespace ModlauncherIV.Core.Catalog;

/// <summary>How strictly the catalog is checked.</summary>
public enum CatalogTrust
{
    /// <summary>
    /// Signed catalogs only. Without a valid signature not a single recipe is
    /// loaded. This is the mode for shipped builds.
    /// </summary>
    RequireSignature,

    /// <summary>
    /// Allow unsigned catalogs. For development only, and only when asked for
    /// explicitly — the caller gets a warning back.
    /// </summary>
    AllowUnsigned,
}

public sealed record CatalogIndexEntry(string File, string Sha256);

public sealed record CatalogIndex(
    int FormatVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CatalogIndexEntry> Entries);

public sealed record SignatureCheck(
    bool Verified,
    CatalogIndex? Index,
    string? Error);

/// <summary>
/// Signs and verifies the recipe catalog.
///
/// Why at all: the catalog decides which files get written into the game
/// directory. Anyone who can swap it can slip in arbitrary code. TLS alone is
/// not enough — it protects the transport, not against a compromised server.
/// Hence a signature over an index listing every recipe file with its checksum,
/// verified against a public key built into the program.
///
/// ECDSA over P-256 with SHA-256, because that ships with .NET without any extra
/// package.
/// </summary>
public static class CatalogSignature
{
    public const string IndexFileName = "index.json";
    public const string SignatureFileName = "index.json.sig";

    /// <summary>
    /// The public key this build trusts (Base64, SPKI).
    ///
    /// The matching private key lives outside the repository and is not handed
    /// out — whoever has it can sign catalogs that every launcher carrying this
    /// embedded key will trust, and thereby decide which files get written into
    /// other people's game directories.
    ///
    /// Changing the key invalidates every catalog signed so far. That is
    /// intended: it is the same operation as a recall.
    /// </summary>
    public const string EmbeddedPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErYJ9SF8sVJWiPILvCWQy/+SzE1/bQWJXGOiaAAlpLv0+PgDLufqQ2zHvWTCsxmkOzoU+eD2ZFaYcFp+Lb4SSPw==";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ------------------------------------------------------------------ Verify

    public static SignatureCheck Verify(string directory, string? publicKeyBase64 = null)
    {
        var key = publicKeyBase64 ?? EmbeddedPublicKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return new SignatureCheck(false, null,
                "This build has no public catalog key built in — "
                + "a signature cannot be verified.");
        }

        var indexPath = Path.Combine(directory, IndexFileName);
        var signaturePath = Path.Combine(directory, SignatureFileName);

        if (!File.Exists(indexPath))
        {
            return new SignatureCheck(false, null, $"{IndexFileName} is missing.");
        }

        if (!File.Exists(signaturePath))
        {
            return new SignatureCheck(false, null, $"{SignatureFileName} is missing.");
        }

        byte[] indexBytes;
        byte[] signature;

        try
        {
            indexBytes = File.ReadAllBytes(indexPath);
            signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            return new SignatureCheck(false, null, $"Signature is not readable: {e.Message}");
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);

            if (!ecdsa.VerifyData(indexBytes, signature, HashAlgorithmName.SHA256))
            {
                return new SignatureCheck(false, null,
                    "The catalog signature is invalid. The catalog was modified, "
                    + "or it did not come from us.");
            }
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return new SignatureCheck(false, null, $"Signature check failed: {e.Message}");
        }

        try
        {
            var index = JsonSerializer.Deserialize<CatalogIndex>(indexBytes, RecipeCatalog.JsonOptions);
            if (index is null)
            {
                return new SignatureCheck(false, null, "The index is empty.");
            }

            return new SignatureCheck(true, index, null);
        }
        catch (JsonException e)
        {
            return new SignatureCheck(false, null, $"Index is not readable: {e.Message}");
        }
    }

    // -------------------------------------------------------- Creating (tools)

    /// <summary>Creates a new key pair. The private part does not belong in the repo.</summary>
    public static (string PrivatePem, string PublicBase64) CreateKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            ecdsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>
    /// Builds the index over every recipe file and signs it. The index is written
    /// in exactly the byte form that is then signed — otherwise the signature
    /// would fail over a difference in formatting.
    /// </summary>
    public static IReadOnlyList<string> Sign(string directory, string privatePem)
    {
        var files = Directory
            .EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFileName(f), IndexFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entries = files
            .Select(f => new CatalogIndexEntry(
                Path.GetRelativePath(directory, f).Replace('\\', '/'),
                Hashing.Sha256File(f)))
            .ToArray();

        var index = new CatalogIndex(1, DateTimeOffset.Now, entries);
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions);

        File.WriteAllBytes(Path.Combine(directory, IndexFileName), indexBytes);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privatePem);

        var signature = ecdsa.SignData(indexBytes, HashAlgorithmName.SHA256);
        File.WriteAllText(Path.Combine(directory, SignatureFileName), Convert.ToBase64String(signature));

        return entries.Select(e => e.File).ToArray();
    }
}
