using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModlauncherIV.Core.Backup;

/// <summary>Eine Datei, die ein Rezept ins Spiel gebracht hat.</summary>
public sealed record OwnedFile(string RelativePath, string? Sha256);

/// <summary>Ein installiertes Rezept.</summary>
public sealed record LedgerEntry(
    string RecipeId,
    string RecipeName,
    string RecipeVersion,
    DateTimeOffset InstalledAt,
    string SnapshotId,
    IReadOnlyList<OwnedFile> Files);

/// <summary>
/// Was der Launcher an dieser Installation verändert hat.
///
/// Das ist die Antwort auf "welche Datei stammt von wem". Ohne diese Zuordnung
/// könnte man ein einzelnes Rezept nicht deinstallieren, ohne die Dateien anderer
/// Rezepte mitzureißen — und man könnte Konflikte nicht erkennen, bevor sie
/// auftreten.
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

    /// <summary>Findet das Rezept, dem eine Datei gehört — für die Konfliktprüfung.</summary>
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
            // Ein kaputtes Ledger darf nicht dazu führen, dass wir den bisherigen
            // Zustand vergessen und munter weiterinstallieren.
            throw new InvalidOperationException(
                $"Das Ledger unter {_file} ist nicht lesbar: {e.Message}", e);
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

        // Erst daneben schreiben, dann ersetzen: ein Absturz mitten im Schreiben
        // darf kein halbes Ledger hinterlassen.
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
