using System.Security.Cryptography;
using System.Text.Json;

namespace ModlauncherIV.Core.Catalog;

/// <summary>Wie streng der Katalog geprüft wird.</summary>
public enum CatalogTrust
{
    /// <summary>
    /// Nur signierte Kataloge. Ohne gültige Signatur wird kein einziges Rezept
    /// geladen. Das ist der Modus für ausgelieferte Stände.
    /// </summary>
    RequireSignature,

    /// <summary>
    /// Unsignierte Kataloge zulassen. Nur für die Entwicklung und nur nach
    /// ausdrücklicher Angabe — der Aufrufer bekommt eine Warnung zurück.
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
/// Signiert und prüft den Rezeptkatalog.
///
/// Warum überhaupt: der Katalog bestimmt, welche Dateien ins Spielverzeichnis
/// geschrieben werden. Wer ihn austauschen kann, kann beliebigen Code
/// unterschieben. TLS allein reicht dafür nicht — es schützt den Transportweg,
/// nicht vor einem übernommenen Server. Deshalb eine Signatur über einen Index,
/// der jede Rezeptdatei mit ihrer Prüfsumme aufführt, geprüft gegen einen im
/// Programm fest eingebauten öffentlichen Schlüssel.
///
/// ECDSA über P-256 mit SHA-256, weil das ohne Zusatzpaket in .NET enthalten ist.
/// </summary>
public static class CatalogSignature
{
    public const string IndexFileName = "index.json";
    public const string SignatureFileName = "index.json.sig";

    /// <summary>
    /// Der öffentliche Schlüssel, dem dieser Build vertraut (Base64, SPKI).
    ///
    /// Noch leer: solange hier kein Schlüssel steht, gibt es keinen signierten
    /// Katalog, und RequireSignature lehnt konsequenterweise alles ab. Sobald ein
    /// Schlüsselpaar erzeugt wurde (mliv catalog-key), kommt der öffentliche Teil
    /// hierher — der private gehört NICHT ins Repository.
    /// </summary>
    public const string EmbeddedPublicKey = "";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ------------------------------------------------------------------ Prüfen

    public static SignatureCheck Verify(string directory, string? publicKeyBase64 = null)
    {
        var key = publicKeyBase64 ?? EmbeddedPublicKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return new SignatureCheck(false, null,
                "Diesem Build ist kein öffentlicher Katalogschlüssel eingebaut — "
                + "eine Signatur kann nicht geprüft werden.");
        }

        var indexPath = Path.Combine(directory, IndexFileName);
        var signaturePath = Path.Combine(directory, SignatureFileName);

        if (!File.Exists(indexPath))
        {
            return new SignatureCheck(false, null, $"{IndexFileName} fehlt.");
        }

        if (!File.Exists(signaturePath))
        {
            return new SignatureCheck(false, null, $"{SignatureFileName} fehlt.");
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
            return new SignatureCheck(false, null, $"Signatur nicht lesbar: {e.Message}");
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);

            if (!ecdsa.VerifyData(indexBytes, signature, HashAlgorithmName.SHA256))
            {
                return new SignatureCheck(false, null,
                    "Die Signatur des Katalogs ist ungültig. Der Katalog wurde verändert "
                    + "oder stammt nicht von uns.");
            }
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return new SignatureCheck(false, null, $"Signaturprüfung fehlgeschlagen: {e.Message}");
        }

        try
        {
            var index = JsonSerializer.Deserialize<CatalogIndex>(indexBytes, RecipeCatalog.JsonOptions);
            if (index is null)
            {
                return new SignatureCheck(false, null, "Der Index ist leer.");
            }

            return new SignatureCheck(true, index, null);
        }
        catch (JsonException e)
        {
            return new SignatureCheck(false, null, $"Index nicht lesbar: {e.Message}");
        }
    }

    // ---------------------------------------------------- Erzeugen (Werkzeug)

    /// <summary>Erzeugt ein neues Schlüsselpaar. Der private Teil gehört nicht ins Repo.</summary>
    public static (string PrivatePem, string PublicBase64) CreateKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            ecdsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>
    /// Baut den Index über alle Rezeptdateien und signiert ihn. Der Index wird in
    /// derselben Byte-Form geschrieben, die anschließend signiert wird — sonst
    /// würde die Signatur an einer anderen Formatierung scheitern.
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
