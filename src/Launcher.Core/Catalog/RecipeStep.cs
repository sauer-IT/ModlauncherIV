using System.IO.Compression;
using System.Text.Json.Serialization;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Core.Catalog;

/// <summary>
/// A single action inside a recipe.
///
/// The contract has three parts, and keeping them apart is the core of the whole
/// safety model:
///
///   <see cref="Describe"/>            — what the user reads in the dry run
///   <see cref="AffectedGamePaths"/>   — which files get touched, BEFORE writing
///   <see cref="Apply"/>               — the actual change
///
/// Because a step can name its targets without changing them, the snapshot can
/// be taken before anything happens. A step that touches files it did not
/// announce makes the rollback incomplete — so every new kind of step has to
/// implement both properly.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EnsureDirectoryStep), "ensureDirectory")]
[JsonDerivedType(typeof(CopyFileStep), "copyFile")]
[JsonDerivedType(typeof(DeleteFileStep), "deleteFile")]
[JsonDerivedType(typeof(ExtractArchiveStep), "extractArchive")]
public abstract record RecipeStep
{
    /// <summary>One line for the dry run.</summary>
    public abstract string Describe();

    /// <summary>
    /// Every game-relative path this step will change — as absolute paths, and
    /// already run through the containment check.
    /// </summary>
    public abstract IReadOnlyList<string> AffectedGamePaths(RecipeContext context);

    /// <summary>Runs the step. Never called during a dry run.</summary>
    public abstract void Apply(RecipeContext context);

    /// <summary>
    /// Checks after the fact whether the result is right. Returns the complaints;
    /// empty means fine. "Copied" is not the same as "copied correctly".
    /// </summary>
    public virtual IReadOnlyList<string> Verify(RecipeContext context) => [];
}

/// <summary>Creates a directory if it is missing.</summary>
public sealed record EnsureDirectoryStep(string Target) : RecipeStep
{
    public override string Describe() => $"Create directory: {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var path = context.ResolveGamePath(Target);
        if (Directory.Exists(path))
        {
            context.Log.Info($"Directory already exists: {Target}");
            return;
        }

        Directory.CreateDirectory(path);
        context.Log.Info($"Directory created: {Target}");
    }
}

/// <summary>Copies an acquired file into the game directory.</summary>
/// <param name="Source">File name inside the working directory.</param>
/// <param name="Target">Target path, relative to the game directory.</param>
public sealed record CopyFileStep(string Source, string Target) : RecipeStep
{
    public override string Describe() => $"Copy: {Source} -> {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var source = context.ResolveSourcePath(Source);
        var target = context.ResolveGamePath(Target);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"The source file is missing from the working directory: {Source}", source);
        }

        var directory = Path.GetDirectoryName(target);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, target, overwrite: true);
        context.Log.Info($"Copied: {Source} -> {Target}");
    }

    public override IReadOnlyList<string> Verify(RecipeContext context)
    {
        var source = context.ResolveSourcePath(Source);
        var target = context.ResolveGamePath(Target);

        if (!File.Exists(target))
        {
            return [$"{Target} was not created."];
        }

        // Byte-for-byte equality against the source: a truncated copy exists
        // just fine, it simply has the wrong content.
        if (File.Exists(source) && Hashing.Sha256File(source) != Hashing.Sha256File(target))
        {
            return [$"{Target} does not match its source {Source}."];
        }

        return [];
    }
}

/// <summary>Removes a file from the game directory.</summary>
public sealed record DeleteFileStep(string Target) : RecipeStep
{
    public override string Describe() => $"Delete: {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var path = context.ResolveGamePath(Target);

        if (!File.Exists(path))
        {
            context.Log.Info($"Not present, nothing to delete: {Target}");
            return;
        }

        File.Delete(path);
        context.Log.Info($"Deleted: {Target}");
    }

    public override IReadOnlyList<string> Verify(RecipeContext context) =>
        File.Exists(context.ResolveGamePath(Target))
            ? [$"{Target} still exists."]
            : [];
}

/// <summary>
/// Extracts an archive into the game directory.
///
/// <see cref="AffectedGamePaths"/> has to read the archive — the affected files
/// are only listed inside it. If the archive is not there yet (dry run before
/// acquisition), the list stays empty and the runner knows it must not execute
/// without the file.
/// </summary>
/// <param name="Archive">Archive name in the working directory.</param>
/// <param name="Target">Target directory relative to the game. Empty = game root.</param>
/// <param name="From">
/// Optional subtree inside the archive. Only entries below it get extracted, and
/// without that prefix. Needed for archives that put everything inside a wrapper
/// folder: without it "Retail/xyz.dll" would land as
/// "&lt;game&gt;/Retail/xyz.dll" instead of "&lt;game&gt;/xyz.dll".
/// </param>
public sealed record ExtractArchiveStep(string Archive, string Target = "", string From = "") : RecipeStep
{
    private string TargetOrRoot => string.IsNullOrWhiteSpace(Target) ? "." : Target;

    /// <summary>The prefix, normalised to forward slashes and with a trailing separator.</summary>
    private string Prefix
    {
        get
        {
            if (string.IsNullOrWhiteSpace(From))
            {
                return string.Empty;
            }

            var normalised = From.Replace('\\', '/').Trim('/');
            return normalised.Length == 0 ? string.Empty : normalised + "/";
        }
    }

    public override string Describe()
    {
        var source = Prefix.Length == 0 ? Archive : $"{Archive}:{Prefix}";
        var target = string.IsNullOrWhiteSpace(Target) ? "(game root)" : Target;
        return $"Extract: {source} -> {target}";
    }

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context)
    {
        var archive = context.ResolveSourcePath(Archive);
        if (!File.Exists(archive))
        {
            return [];
        }

        var targetRoot = context.ResolveGamePath(TargetOrRoot);

        try
        {
            using var zip = ZipFile.OpenRead(archive);
            return Selected(zip)
                .Select(e => ResolveEntry(targetRoot, e.Relative))
                .ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new RecipeSecurityException($"Archive is not readable: {Archive} ({e.Message})");
        }
    }

    public override void Apply(RecipeContext context)
    {
        var archive = context.ResolveSourcePath(Archive);
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException(
                $"The archive is missing from the working directory: {Archive}", archive);
        }

        var targetRoot = context.ResolveGamePath(TargetOrRoot);
        Directory.CreateDirectory(targetRoot);

        using var zip = ZipFile.OpenRead(archive);
        var count = 0;

        foreach (var (entry, relative) in Selected(zip))
        {
            var destination = ResolveEntry(targetRoot, relative);
            var directory = Path.GetDirectoryName(destination);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destination, overwrite: true);
            count++;
        }

        if (count == 0)
        {
            // A subtree that matches nothing is a recipe bug, not a quiet
            // success — otherwise the recipe reports "done" without any effect.
            throw new FileNotFoundException(
                $"Nothing inside archive {Archive} sits under '{Prefix}'.", archive);
        }

        context.Log.Info($"Extracted: {Archive} ({count} files) -> {TargetOrRoot}");
    }

    /// <summary>The entries this step covers, with their target path minus the prefix.</summary>
    private IEnumerable<(ZipArchiveEntry Entry, string Relative)> Selected(ZipArchive zip)
    {
        var prefix = Prefix;

        foreach (var entry in zip.Entries)
        {
            // Directory entries end in '/' and have no name.
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var full = entry.FullName.Replace('\\', '/');

            if (prefix.Length == 0)
            {
                yield return (entry, full);
                continue;
            }

            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return (entry, full[prefix.Length..]);
            }
        }
    }

    public override IReadOnlyList<string> Verify(RecipeContext context)
    {
        var missing = AffectedGamePaths(context)
            .Where(p => !File.Exists(p))
            .Select(p => $"{Path.GetRelativePath(context.GameRoot, p)} is missing after extraction.")
            .ToArray();

        return missing;
    }

    /// <summary>
    /// Archive entries are foreign data. "..\..\windows\system32\x.dll" as an
    /// entry name is a well-known attack (Zip Slip), so it is checked here just
    /// as strictly as any recipe path.
    /// </summary>
    private static string ResolveEntry(string targetRoot, string entryName)
    {
        var full = Path.GetFullPath(Path.Combine(targetRoot, entryName));
        var prefix = targetRoot.EndsWith(Path.DirectorySeparatorChar)
            ? targetRoot
            : targetRoot + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeSecurityException(
                $"Archive entry points outside the target directory: {entryName}");
        }

        return full;
    }
}
