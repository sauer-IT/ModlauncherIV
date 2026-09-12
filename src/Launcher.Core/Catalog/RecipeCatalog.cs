using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModlauncherIV.Core.Catalog;

public sealed record CatalogLoadResult(
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<string> Errors)
{
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

    public static CatalogLoadResult LoadFrom(string directory)
    {
        var recipes = new List<Recipe>();
        var errors = new List<string>();

        if (!Directory.Exists(directory))
        {
            return new CatalogLoadResult([], [$"Katalogverzeichnis nicht gefunden: {directory}"]);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order())
        {
            var name = Path.GetFileName(file);

            try
            {
                var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllText(file), JsonOptions);
                if (recipe is null)
                {
                    errors.Add($"{name}: leer.");
                    continue;
                }

                var problems = Validate(recipe);
                if (problems.Count > 0)
                {
                    errors.Add($"{name}: {string.Join("; ", problems)}");
                    continue;
                }

                recipes.Add(recipe);
            }
            catch (Exception e) when (e is IOException or JsonException or NotSupportedException)
            {
                errors.Add($"{name}: {e.Message}");
            }
        }

        foreach (var duplicate in recipes
                     .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Rezept-ID mehrfach vergeben: {duplicate.Key}");
        }

        return new CatalogLoadResult(recipes, errors);
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
