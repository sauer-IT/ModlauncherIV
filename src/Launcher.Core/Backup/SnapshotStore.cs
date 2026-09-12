using System.Security.Cryptography;
using System.Text.Json;

namespace ModlauncherIV.Core.Backup;

/// <summary>
/// The state of one file before it was changed.
/// <paramref name="Existed"/> false means: the file did not exist, so the
/// rollback has to delete it rather than write it back. Without that
/// distinction every newly created file would survive a failed run.
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
/// Backs files up before they are changed, and restores them again.
///
/// REAL COPIES on purpose rather than hard links, even though that costs more
/// space. A hard link shares its content with the original: if a step writes
/// into the existing file instead of replacing it — and that is exactly what
/// File.Copy with overwrite does — the "snapshot" changes along with it. The
/// backup would be worthless at the very moment it is needed.
/// </summary>
public sealed class SnapshotStore(string gameRoot)
{
    private readonly string _gameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar);
    private readonly string _root = AppPaths.SnapshotsFor(gameRoot);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DirectoryFor(string id) => Path.Combine(_root, id);

    /// <summary>
    /// Backs up every given path. Duplicate entries are merged so that a file is
    /// not backed up twice and written back in the wrong order during a rollback.
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
    /// Restores a snapshot. Does not throw on the first error but tries every
    /// file — a half-restored state is worse than one where a single file fails
    /// and gets reported.
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

    /// <summary>Total size of every backup belonging to this installation.</summary>
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
