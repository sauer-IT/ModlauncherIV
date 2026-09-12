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
/// Loads recipes from a directory. A broken file leaves the others alone and is
/// reported as an error — one faulty recipe must not render the launcher
/// unusable.
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
    /// Loads the catalog. With <see cref="CatalogTrust.RequireSignature"/> not a
    /// single recipe is loaded without a valid signature — not "the harmless ones
    /// anyway", because whoever can forge the catalog picks which ones look
    /// harmless.
    /// </summary>
    public static CatalogLoadResult LoadFrom(
        string directory,
        CatalogTrust trust = CatalogTrust.RequireSignature,
        string? publicKey = null)
    {
        if (!Directory.Exists(directory))
        {
            return CatalogLoadResult.Rejected($"Catalog directory not found: {directory}");
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
                $"Catalog is not trusted: {check.Error} "
                + "(--allow-unsigned overrides this for development.)");
        }

        var recipes = new List<Recipe>();
        var errors = new List<string>();

        foreach (var entry in check.Index.Entries)
        {
            // The entry path comes out of a signed file, but it is still a path —
            // it must not lead out of the catalog directory.
            var file = Path.GetFullPath(Path.Combine(directory, entry.File));
            var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{entry.File}: points outside the catalog directory.");
                continue;
            }

            if (!File.Exists(file))
            {
                errors.Add($"{entry.File}: listed in the index but not present.");
                continue;
            }

            var actual = Hashing.Sha256File(file);
            if (!Hashing.Equal(actual, entry.Sha256))
            {
                errors.Add($"{entry.File}: checksum differs from the signed index.");
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
            ["The catalog was NOT checked against a signature. For development only."],
            SignatureVerified: false);
    }

    /// <summary>A broken file leaves the others alone and gets reported.</summary>
    private static void Read(string file, List<Recipe> recipes, List<string> errors)
    {
        var name = Path.GetFileName(file);

        try
        {
            var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllText(file), JsonOptions);
            if (recipe is null)
            {
                errors.Add($"{name}: empty.");
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
            errors.Add($"Recipe id used more than once: {duplicate.Key}");
        }
    }

    /// <summary>
    /// Checks what can be checked without executing anything. A source without a
    /// checksum is the case that matters most: it would let unverified files into
    /// the game.
    /// </summary>
    private static IReadOnlyList<string> Validate(Recipe recipe)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(recipe.Id))
        {
            problems.Add("no id");
        }

        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            problems.Add("no name");
        }

        if (string.IsNullOrWhiteSpace(recipe.Version))
        {
            problems.Add("no version");
        }

        if (recipe.Actions.Count == 0)
        {
            problems.Add("no steps");
        }

        foreach (var source in recipe.RequiredFiles)
        {
            if (source.Sha256.Length != 64 || !source.Sha256.All(Uri.IsHexDigit))
            {
                problems.Add($"source '{source.Id}' has no valid SHA-256 checksum");
            }

            if (source.SizeBytes <= 0)
            {
                problems.Add($"source '{source.Id}' has no size");
            }
        }

        return problems;
    }
}
