using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModlauncherIV.Core.Backup;

/// <summary>A file a recipe brought into the game.</summary>
public sealed record OwnedFile(string RelativePath, string? Sha256);

/// <summary>An installed recipe.</summary>
public sealed record LedgerEntry(
    string RecipeId,
    string RecipeName,
    string RecipeVersion,
    DateTimeOffset InstalledAt,
    string SnapshotId,
    IReadOnlyList<OwnedFile> Files,

    /// <summary>Game version after the run. Later reveals whether the store patched it back.</summary>
    string? GameVersionAfter = null);

/// <summary>
/// What the launcher changed about this installation.
///
/// This is the answer to "which file came from whom". Without that mapping you
/// could not uninstall a single recipe without tearing other recipes' files out
/// with it — and you could not detect conflicts before they happen.
/// </summary>
public sealed record InstallLedger(
    string GameRoot,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LedgerEntry> Entries)
{
    public static InstallLedger Empty(string gameRoot) =>
        new(gameRoot, DateTimeOffset.Now, []);

    public bool IsInstalled(string recipeId) =>
        Entries.Any(e => string.Equals(e.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase));

    public LedgerEntry? Find(string recipeId) =>
        Entries.FirstOrDefault(e => string.Equals(e.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the recipe a file belongs to — for the conflict check.</summary>
    public LedgerEntry? OwnerOf(string relativePath) =>
        Entries.FirstOrDefault(e => e.Files.Any(f =>
            string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)));
}

public sealed class LedgerStore(string gameRoot)
{
    private readonly string _gameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar);
    private readonly string _file = AppPaths.LedgerFor(gameRoot);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => _file;

    /// <summary>
    /// Every installation the launcher has ever written to, read from the
    /// ledgers rather than from detection.
    ///
    /// The difference matters exactly once, and badly: at uninstall time.
    /// Detection answers "which installations are on this machine now" and goes
    /// quiet when there are two of them or when a disk is not plugged in. The
    /// ledgers answer "which installations are owed something", which is the
    /// question that decides whether the snapshots may be thrown away.
    /// </summary>
    /// <param name="root">
    /// Where to look. Defaults to the launcher's own state folder; a test
    /// points it somewhere else.
    /// </param>
    public static IReadOnlyList<InstallLedger> All(string? root = null)
    {
        var installs = Path.Combine(root ?? AppPaths.Root, "installs");

        if (!Directory.Exists(installs))
        {
            return [];
        }

        var ledgers = new List<InstallLedger>();

        foreach (var file in SafeDirectories(installs).Select(d => Path.Combine(d, "ledger.json")))
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var ledger = JsonSerializer.Deserialize<InstallLedger>(File.ReadAllText(file), JsonOptions);

                if (ledger is not null && !string.IsNullOrWhiteSpace(ledger.GameRoot))
                {
                    ledgers.Add(ledger);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // One unreadable ledger is one installation this cannot speak
                // for. Reporting nothing at all because of it would be worse.
            }
        }

        return ledgers;
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public InstallLedger Load()
    {
        if (!File.Exists(_file))
        {
            return InstallLedger.Empty(_gameRoot);
        }

        try
        {
            return JsonSerializer.Deserialize<InstallLedger>(File.ReadAllText(_file), JsonOptions)
                   ?? InstallLedger.Empty(_gameRoot);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // A broken ledger must not make us forget the previous state and cheerfully
            // carry on installing.
            throw new InvalidOperationException(
                $"The ledger at {_file} is not readable: {e.Message}", e);
        }
    }

    public void Save(InstallLedger ledger)
    {
        var directory = Path.GetDirectoryName(_file);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var updated = ledger with { UpdatedAt = DateTimeOffset.Now };

        // Write beside it first, then replace: a crash while writing must not leave
        // half a ledger behind.
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(updated, JsonOptions));
        File.Move(temp, _file, overwrite: true);
    }

    public InstallLedger Add(InstallLedger ledger, LedgerEntry entry)
    {
        var remaining = ledger.Entries
            .Where(e => !string.Equals(e.RecipeId, entry.RecipeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        remaining.Add(entry);
        return ledger with { Entries = remaining };
    }

    public InstallLedger Remove(InstallLedger ledger, string recipeId) =>
        ledger with
        {
            Entries = ledger.Entries
                .Where(e => !string.Equals(e.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        };
}
