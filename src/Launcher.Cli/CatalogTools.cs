using System.Security.Cryptography;
using System.Text;
using ModlauncherIV.Core.Catalog;

namespace ModlauncherIV.Cli;

/// <summary>
/// Tools for maintaining the catalog. Not meant for end users but for whoever
/// publishes the catalog.
/// </summary>
internal static class CatalogTools
{
    public static int CreateKey(CliOptions options)
    {
        var (privatePem, publicBase64) = CatalogSignature.CreateKey();
        var target = options.KeyPath;

        if (target is null)
        {
            Console.Error.WriteLine("Use --key to say where the private key should go.");
            Console.Error.WriteLine("A path OUTSIDE the repository.");
            return ExitCode.BadUsage;
        }

        var full = Path.GetFullPath(target);

        if (File.Exists(full))
        {
            // Overwriting an existing key makes every catalog signed with it
            // useless. That does not happen by accident.
            Console.Error.WriteLine($"A file already exists there: {full}");
            Console.Error.WriteLine("Not overwritten — existing signatures would become invalid.");
            return ExitCode.Failed;
        }

        var directory = Path.GetDirectoryName(full);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, privatePem);

        Console.WriteLine($"Private key written: {full}");
        Console.WriteLine();
        Console.WriteLine("Do NOT put this key in the repository and do not pass it on.");
        Console.WriteLine("Whoever has it can sign catalogs that the launcher trusts.");
        Console.WriteLine();
        Console.WriteLine("Put the public part into CatalogSignature.EmbeddedPublicKey:");
        Console.WriteLine();
        Console.WriteLine($"    public const string EmbeddedPublicKey = \"{publicBase64}\";");
        Console.WriteLine();

        return ExitCode.Ok;
    }

    public static int Sign(CliOptions options)
    {
        if (options.KeyPath is null)
        {
            Console.Error.WriteLine("Use --key to give the private key.");
            return ExitCode.BadUsage;
        }

        var directory = options.CatalogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "catalog");

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Catalog directory not found: {directory}");
            return ExitCode.NothingFound;
        }

        if (!File.Exists(options.KeyPath))
        {
            Console.Error.WriteLine($"Key file not found: {options.KeyPath}");
            return ExitCode.NothingFound;
        }

        var signed = CatalogSignature.Sign(directory, File.ReadAllText(options.KeyPath));

        Console.WriteLine($"Catalog signed: {Path.GetFullPath(directory)}");
        Console.WriteLine($"{signed.Count} file(s) in the index:");

        foreach (var file in signed)
        {
            Console.WriteLine($"  {file}");
        }

        return ExitCode.Ok;
    }

    /// <summary>
    /// Writes an encrypted copy of the signing key.
    ///
    /// Asks for the passphrase twice and decrypts the result again before saying
    /// done: a backup that turns out not to open is found out on the day it is
    /// needed, which is the worst day to find it out.
    /// </summary>
    public static int BackupKey(CliOptions options)
    {
        if (options.KeyPath is null || options.OutputFile is null)
        {
            Console.Error.WriteLine("Use --key for the signing key and --out for where the encrypted copy goes.");
            return ExitCode.BadUsage;
        }

        if (!File.Exists(options.KeyPath))
        {
            Console.Error.WriteLine($"Key file not found: {options.KeyPath}");
            return ExitCode.NothingFound;
        }

        var target = Path.GetFullPath(options.OutputFile);

        if (File.Exists(target))
        {
            Console.Error.WriteLine($"A file already exists there: {target}");
            Console.Error.WriteLine("Not overwritten - it may be the only other copy.");
            return ExitCode.Failed;
        }

        var privatePem = File.ReadAllText(options.KeyPath);
        string publicKey;

        try
        {
            publicKey = CatalogKeyBackup.PublicKeyOf(privatePem);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            Console.Error.WriteLine($"That file is not a readable private key: {e.Message}");
            return ExitCode.Failed;
        }

        DescribeKey(publicKey);

        var passphrase = ReadPassphrase("Passphrase: ");
        var again = ReadPassphrase("Once more:  ");

        if (!string.Equals(passphrase, again, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The two passphrases differ. Nothing written.");
            return ExitCode.Failed;
        }

        if (passphrase.Length < CatalogKeyBackup.MinimumPassphraseLength)
        {
            Console.Error.WriteLine(
                $"At least {CatalogKeyBackup.MinimumPassphraseLength} characters - a few words are easier "
                + "to keep than a short string of symbols. Nothing written.");
            return ExitCode.BadUsage;
        }

        var encrypted = CatalogKeyBackup.Encrypt(privatePem, passphrase);

        if (CatalogKeyBackup.PublicKeyOf(CatalogKeyBackup.Decrypt(encrypted, passphrase)) != publicKey)
        {
            Console.Error.WriteLine("The encrypted copy does not open to the same key. Nothing written.");
            return ExitCode.Failed;
        }

        var directory = Path.GetDirectoryName(target);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(target, encrypted);

        Console.WriteLine($"Encrypted copy written: {target}");
        Console.WriteLine("Checked: it opens with that passphrase to the same key.");
        Console.WriteLine();
        Console.WriteLine("Keep this file in two places, and the passphrase only in a password manager.");
        Console.WriteLine("Without the passphrase the file is useless - to anyone, you included.");

        return ExitCode.Ok;
    }

    /// <summary>Puts the signing key back from an encrypted copy.</summary>
    public static int RestoreKey(CliOptions options)
    {
        if (options.Argument is null || options.KeyPath is null)
        {
            Console.Error.WriteLine("Usage: mliv catalog-key-restore <encrypted-file> --key <where the key goes>");
            return ExitCode.BadUsage;
        }

        if (!File.Exists(options.Argument))
        {
            Console.Error.WriteLine($"Backup not found: {options.Argument}");
            return ExitCode.NothingFound;
        }

        var target = Path.GetFullPath(options.KeyPath);

        if (File.Exists(target))
        {
            Console.Error.WriteLine($"A key already exists there: {target}");
            Console.Error.WriteLine("Not overwritten - move it away first if it is really meant to go.");
            return ExitCode.Failed;
        }

        var passphrase = ReadPassphrase("Passphrase: ");
        string privatePem;

        try
        {
            privatePem = CatalogKeyBackup.Decrypt(File.ReadAllText(options.Argument), passphrase);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            Console.Error.WriteLine("Wrong passphrase, or the file is not an encrypted key. Nothing written.");
            return ExitCode.Failed;
        }

        DescribeKey(CatalogKeyBackup.PublicKeyOf(privatePem));

        var directory = Path.GetDirectoryName(target);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(target, privatePem);
        Console.WriteLine($"Key restored: {target}");

        return ExitCode.Ok;
    }

    private static void DescribeKey(string publicKey)
    {
        Console.WriteLine($"Public key  {publicKey}");
        Console.WriteLine(publicKey == CatalogSignature.EmbeddedPublicKey
            ? "            the one this build trusts"
            : "            NOT the one this build trusts - catalogs signed with it will be refused");
    }

    /// <summary>
    /// Reads a passphrase without echoing it. From a pipe it reads a line, so the
    /// commands can be tested; typed at a console, nothing appears on screen.
    /// </summary>
    private static string ReadPassphrase(string prompt)
    {
        Console.Error.Write(prompt);

        if (Console.IsInputRedirected)
        {
            // Windows PowerShell puts a byte order mark in front of what it pipes
            // into a program. Left in, the first of two identical passphrases is
            // one invisible character longer than the second.
            return (Console.ReadLine() ?? string.Empty).TrimStart('﻿');
        }

        var typed = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return typed.ToString();
    }
}
