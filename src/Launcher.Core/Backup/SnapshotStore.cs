using System.Security.Cryptography;
using System.Text.Json;

namespace ModlauncherIV.Core.Backup;

/// <summary>
/// Der Zustand einer einzelnen Datei vor der Änderung.
/// <paramref name="Existed"/> false bedeutet: die Datei gab es nicht, der Rollback
/// muss sie also löschen statt zurückzuschreiben. Ohne diese Unterscheidung
/// bliebe nach einem fehlgeschlagenen Lauf jede neu angelegte Datei liegen.
/// </summary>
public sealed record SnapshotEntry(
    string RelativePath,
    bool Existed,
    bool WasDirectory,
    long SizeBytes,
    string? Sha256);

public sealed record Snapshot(
    string Id,
    string GameRoot,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SnapshotEntry> Entries);

/// <summary>
/// Sichert Dateien, bevor sie verändert werden, und stellt sie wieder her.
///
/// Bewusst ECHTE KOPIEN statt Hardlinks, auch wenn das mehr Platz kostet.
/// Ein Hardlink teilt sich den Inhalt mit dem Original: schreibt ein Schritt in
/// die bestehende Datei, statt sie zu ersetzen — und genau das tut etwa
/// File.Copy mit overwrite — dann ändert sich der "Snapshot" mit. Die Sicherung
/// wäre in dem Moment wertlos, in dem man sie braucht.
/// </summary>
public sealed class SnapshotStore(string gameRoot)
{
    private readonly string _gameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar);
    private readonly string _root = AppPaths.SnapshotsFor(gameRoot);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DirectoryFor(string id) => Path.Combine(_root, id);

    /// <summary>
    /// Sichert alle angegebenen Pfade. Doppelte Einträge werden zusammengefasst,
    /// damit eine Datei nicht zweimal gesichert und beim Rollback in der falschen
    /// Reihenfolge zurückgeschrieben wird.
    /// </summary>
    public Snapshot Create(IEnumerable<string> absolutePaths, string? id = null)
    {
        id ??= $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        var directory = DirectoryFor(id);
        var fileRoot = Path.Combine(directory, "files");
        Directory.CreateDirectory(fileRoot);

        var entries = new List<SnapshotEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in absolutePaths)
        {
            var full = Path.GetFullPath(path);
            if (!seen.Add(full))
            {
                continue;
            }

            entries.Add(Capture(full, fileRoot));
        }

        var snapshot = new Snapshot(id, _gameRoot, DateTimeOffset.Now, entries);
        File.WriteAllText(
            Path.Combine(directory, "snapshot.json"),
            JsonSerializer.Serialize(snapshot, JsonOptions));

        return snapshot;
    }

    private SnapshotEntry Capture(string absolutePath, string fileRoot)
    {
        var relative = Path.GetRelativePath(_gameRoot, absolutePath);

        if (Directory.Exists(absolutePath))
        {
            return new SnapshotEntry(relative, Existed: true, WasDirectory: true, 0, null);
        }

        if (!File.Exists(absolutePath))
        {
            return new SnapshotEntry(relative, Existed: false, WasDirectory: false, 0, null);
        }

        var stored = Path.Combine(fileRoot, relative);
        var storedDirectory = Path.GetDirectoryName(stored);
        if (storedDirectory is not null)
        {
            Directory.CreateDirectory(storedDirectory);
        }

        File.Copy(absolutePath, stored, overwrite: true);

        var info = new FileInfo(stored);
        using var stream = File.OpenRead(stored);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        return new SnapshotEntry(relative, Existed: true, WasDirectory: false, info.Length, hash);
    }

    /// <summary>
    /// Spielt einen Snapshot zurück. Wirft nicht beim ersten Fehler, sondern
    /// versucht jede Datei — ein halb wiederhergestellter Zustand ist schlechter
    /// als einer, bei dem eine einzelne Datei fehlschlägt und gemeldet wird.
    /// </summary>
    public IReadOnlyList<string> Restore(Snapshot snapshot)
    {
        var fileRoot = Path.Combine(DirectoryFor(snapshot.Id), "files");
        var failures = new List<string>();

        foreach (var entry in snapshot.Entries)
        {
            var target = Path.Combine(_gameRoot, entry.RelativePath);

            try
            {
                if (!entry.Existed)
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    else if (Directory.Exists(target))
                    {
                        Directory.Delete(target, recursive: true);
                    }

                    continue;
                }

                if (entry.WasDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                var stored = Path.Combine(fileRoot, entry.RelativePath);
                if (!File.Exists(stored))
                {
                    failures.Add($"{entry.RelativePath}: Sicherungskopie fehlt");
                    continue;
                }

                var directory = Path.GetDirectoryName(target);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(stored, target, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{entry.RelativePath}: {e.Message}");
            }
        }

        return failures;
    }

    public Snapshot? Load(string id)
    {
        var file = Path.Combine(DirectoryFor(id), "snapshot.json");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file), JsonOptions);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Gesamtgröße aller Sicherungen dieser Installation.</summary>
    public long TotalSizeBytes()
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        try
        {
            return new DirectoryInfo(_root)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
