using System.Text.Json;

namespace ModlauncherIV.Core.Catalog;

/// <summary>How to tell whether a tool is already on this machine.</summary>
/// <param name="RegistryKey">Under HKCU, without the hive.</param>
/// <param name="RegistryValue">The value holding its installation directory.</param>
/// <param name="Executable">
/// What has to exist inside that directory. A registry key outlives an
/// uninstall; the file does not.
/// </param>
public sealed record ToolDetection(string RegistryKey, string RegistryValue, string Executable);

/// <summary>
/// A program next to the game that the launcher can fetch but does not install
/// itself.
///
/// Deliberately not a <see cref="Recipe"/>, and kept in its own folder for the
/// same reason. A recipe is a change to the game directory: every path it names
/// goes through the containment check, everything it writes is in a snapshot
/// first, and it can be taken back file by file. None of that is true here. The
/// launcher downloads an installer, checks it against a checksum, and hands it
/// over - what happens next belongs to that installer, and the launcher cannot
/// undo it.
///
/// Calling this a recipe would make the wizard's promise - "you can undo this at
/// any time" - untrue for one entry in the list, which is worse than not
/// offering it at all. Hence two kinds, two lists, and two sentences about what
/// each can do.
/// </summary>
/// <param name="Installer">
/// The archive or executable to fetch. Checked against its checksum exactly like
/// a recipe source, because that part is the same problem.
/// </param>
/// <param name="InstallerEntry">
/// The file inside the archive to run. Empty when the download is already the
/// executable.
/// </param>
public sealed record CatalogTool(
    string Id,
    string Name,
    string Version,
    string Description,
    RecipeSource Installer,
    ToolDetection Detect,
    string InstallerEntry = "",
    string? Note = null);

/// <summary>
/// Reads the tools from <c>catalog/tools</c>.
///
/// Same folder as the recipes and therefore the same signature: the index covers
/// every .json below the catalog directory, so a tool cannot be added or changed
/// without breaking it. That matters more here than for a recipe - this ends in
/// an executable being started.
/// </summary>
public static class ToolCatalog
{
    public const string FolderName = "tools";

    public static IReadOnlyList<CatalogTool> LoadFrom(string catalogDirectory, CatalogLoadResult catalog)
    {
        // No valid signature, no tools. A recipe writes files we can undo; this
        // starts a program we cannot.
        if (!catalog.SignatureVerified)
        {
            return [];
        }

        var directory = Path.Combine(catalogDirectory, FolderName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var tools = new List<CatalogTool>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order())
        {
            try
            {
                var tool = JsonSerializer.Deserialize<CatalogTool>(
                    File.ReadAllText(file), RecipeCatalog.JsonOptions);

                if (tool is not null && tool.Id.Length > 0 && tool.Installer.Sha256.Length > 0)
                {
                    tools.Add(tool);
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken tool file leaves the rest alone. Nothing is lost by
                // skipping it: a tool that cannot be read is one the launcher
                // simply does not offer.
            }
        }

        return tools;
    }
}
