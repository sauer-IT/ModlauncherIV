using System.Security.Cryptography;

namespace ModlauncherIV.Core.Catalog;

/// <summary>
/// An encrypted copy of the catalog signing key.
///
/// The key is the one thing that cannot be rebuilt: its public half is compiled
/// into every launcher already handed out, so losing it means those copies never
/// accept another catalog. A plain copy on a USB stick or in cloud storage is
/// the other failure - anyone who finds it signs catalogs every launcher trusts.
/// Encrypted, it can sit in both places, and the passphrase lives somewhere
/// else. Neither half alone signs anything.
///
/// Standard PKCS#8 with AES-256 and PBKDF2 over SHA-256, so the file stays
/// readable by OpenSSL and anything else long after this program is gone.
/// </summary>
public static class CatalogKeyBackup
{
    /// <summary>Below this a passphrase is guessable faster than the iterations help.</summary>
    public const int MinimumPassphraseLength = 12;

    private const int Iterations = 600_000;

    public static string Encrypt(string privatePem, ReadOnlySpan<char> passphrase)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privatePem);

        return ecdsa.ExportEncryptedPkcs8PrivateKeyPem(
            passphrase,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, Iterations));
    }

    /// <summary>The plain key again. A wrong passphrase throws <see cref="CryptographicException"/>.</summary>
    public static string Decrypt(string encryptedPem, ReadOnlySpan<char> passphrase)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromEncryptedPem(encryptedPem, passphrase);

        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>The public half, in the form <see cref="CatalogSignature.EmbeddedPublicKey"/> uses.</summary>
    public static string PublicKeyOf(string privatePem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privatePem);

        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }
}
