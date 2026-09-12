using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModlauncherIV.Core.Catalog;

public sealed record CatalogLoadResult(
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    bool SignatureVerified)
{
    public static CatalogLoadResult Rejected(string error) => new([], [error], [], false);

    public Recipe? Find(string id) =>
        Recipes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Lädt Rezepte aus einem Verzeichnis. Eine kaputte Datei lässt die anderen
/// unberührt und wird als Fehler gemeldet — ein einzelnes fehlerhaftes Rezept
/// darf den Launcher nicht unbrauchbar machen.
/// </summary>
public static class RecipeCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Lädt den Katalog. Bei <see cref="CatalogTrust.RequireSignature"/> wird ohne
    /// gültige Signatur kein einziges Rezept geladen — nicht "die guten trotzdem",
    /// denn wer den Katalog fälschen kann, sucht sich aus, welche gut aussehen.
    /// </summary>
    public static CatalogLoadResult LoadFrom(
        string directory,
        CatalogTrust trust = CatalogTrust.RequireSignature,
        string? publicKey = null)
    {
        if (!Directory.Exists(directory))
        {
            return CatalogLoadResult.Rejected($"Katalogverzeichnis nicht gefunden: {directory}");
        }

        return trust == CatalogTrust.RequireSignature
            ? LoadSigned(directory, publicKey)
            : LoadUnsigned(directory);
    }

    private static CatalogLoadResult LoadSigned(string directory, string? publicKey)
    {
        var check = CatalogSignature.Verify(directory, publicKey);

        if (!check.Verified || check.Index is null)
        {
            return CatalogLoadResult.Rejected(
                $"Katalog nicht vertrauenswürdig: {check.Error} "
                + "(Mit --allow-unsigned lässt sich das für die Entwicklung übergehen.)");
        }

        var recipes = new List<Recipe>();
        var errors = new List<string>();

        foreach (var entry in check.Index.Entries)
        {
            // Der Eintragspfad kommt aus einer signierten Datei, ist aber trotzdem
            // ein Pfad — er darf nicht aus dem Katalogverzeichnis herausführen.
            var file = Path.GetFullPath(Path.Combine(directory, entry.File));
            var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{entry.File}: zeigt aus dem Katalogverzeichnis heraus.");
                continue;
            }

            if (!File.Exists(file))
            {
                errors.Add($"{entry.File}: im Index aufgeführt, aber nicht vorhanden.");
                continue;
            }

            var actual = Hashing.Sha256File(file);
            if (!Hashing.Equal(actual, entry.Sha256))
            {
                errors.Add($"{entry.File}: Prüfsumme weicht vom signierten Index ab.");
                continue;
            }

            Read(file, recipes, errors);
        }

        CheckDuplicates(recipes, errors);
        return new CatalogLoadResult(recipes, errors, [], SignatureVerified: true);
    }

    private static CatalogLoadResult LoadUnsigned(string directory)
    {
        var recipes = new List<Recipe>();
        var errors = new List<string>();

        var files = Directory
            .EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Where(f => !string.Equals(
                Path.GetFileName(f), CatalogSignature.IndexFileName, StringComparison.OrdinalIgnoreCase))
            .Order();

        foreach (var file in files)
        {
            Read(file, recipes, errors);
        }

        CheckDuplicates(recipes, errors);

        return new CatalogLoadResult(
            recipes,
            errors,
            ["Der Katalog wurde NICHT auf eine Signatur geprüft. Nur für die Entwicklung."],
            SignatureVerified: false);
    }

    /// <summary>Eine kaputte Datei lässt die anderen unberührt und wird gemeldet.</summary>
    private static void Read(string file, List<Recipe> recipes, List<string> errors)
    {
        var name = Path.GetFileName(file);

        try
        {
            var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllText(file), JsonOptions);
            if (recipe is null)
            {
                errors.Add($"{name}: leer.");
                return;
            }

            var problems = Validate(recipe);
            if (problems.Count > 0)
            {
                errors.Add($"{name}: {string.Join("; ", problems)}");
                return;
            }

            recipes.Add(recipe);
        }
        catch (Exception e) when (e is IOException or JsonException or NotSupportedException)
        {
            errors.Add($"{name}: {e.Message}");
        }
    }

    private static void CheckDuplicates(List<Recipe> recipes, List<string> errors)
    {
        foreach (var duplicate in recipes
                     .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Rezept-ID mehrfach vergeben: {duplicate.Key}");
        }
    }

    /// <summary>
    /// Prüft, was ohne Ausführung prüfbar ist. Eine Quelle ohne Prüfsumme ist der
    /// wichtigste Fall: sie würde ungeprüfte Dateien ins Spiel lassen.
    /// </summary>
    private static IReadOnlyList<string> Validate(Recipe recipe)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(recipe.Id))
        {
            problems.Add("keine Id");
        }

        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            problems.Add("kein Name");
        }

        if (string.IsNullOrWhiteSpace(recipe.Version))
        {
            problems.Add("keine Version");
        }

        if (recipe.Actions.Count == 0)
        {
            problems.Add("keine Schritte");
        }

        foreach (var source in recipe.RequiredFiles)
        {
            if (source.Sha256.Length != 64 || !source.Sha256.All(Uri.IsHexDigit))
            {
                problems.Add($"Quelle '{source.Id}' hat keine gültige SHA-256-Prüfsumme");
            }

            if (source.SizeBytes <= 0)
            {
                problems.Add($"Quelle '{source.Id}' hat keine Größenangabe");
            }
        }

        return problems;
    }
}
