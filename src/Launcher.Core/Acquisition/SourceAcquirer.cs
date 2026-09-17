using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Core.Acquisition;

public enum AcquisitionStatus
{
    /// <summary>Was already in the working directory with the right checksum.</summary>
    AlreadyPresent,

    Downloaded,

    /// <summary>
    /// Came out of the launcher's own payload — nothing downloaded, but measured
    /// against the recipe's checksum exactly like everything else.
    /// </summary>
    Bundled,

    /// <summary>
    /// Found among the files the user was asked to supply, by checksum rather
    /// than by name. Nexus and MEGA do not allow a program to download for you,
    /// so for those the user downloads and the launcher looks - in the working
    /// directory and in the download folder.
    /// </summary>
    Supplied,

    /// <summary>No source reachable — the user has to supply the file.</summary>
    NeedsUserAction,

    Failed,
}

public sealed record AcquisitionResult(
    RecipeSource Source,
    AcquisitionStatus Status,
    string? Path,
    IReadOnlyList<string> Attempts,
    string? Error)
{
    public bool Ok => Status is AcquisitionStatus.AlreadyPresent or AcquisitionStatus.Downloaded
        or AcquisitionStatus.Bundled or AcquisitionStatus.Supplied;
}

public sealed record AcquisitionProgress(string FileName, long BytesRead, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0 ? (int)(BytesRead * 100 / TotalBytes.Value) : null;
}

/// <summary>
/// Acquires the files a recipe needs.
///
/// The rule: a file only counts as present once its SHA-256 matches. An existing
/// file with the wrong checksum is discarded and fetched again — it is either
/// truncated or swapped, and neither belongs in the game directory.
///
/// Downloads always land beside the target (.part) and are only moved into place
/// after the check passes. An abort therefore never leaves behind half a file
/// that the next run would mistake for complete.
/// </summary>
/// <param name="bundledRoot">
/// Folder holding files the launcher brings along itself — its own trainer, for
/// instance. Null when there is none.
/// </param>
public sealed class SourceAcquirer(
    HttpClient http,
    string cacheRoot,
    IExecutionLog log,
    string? bundledRoot = null,
    IReadOnlyList<string>? lookIn = null)
{
    private readonly string _cache = Path.GetFullPath(cacheRoot);
    private readonly string? _bundled = bundledRoot is null ? null : Path.GetFullPath(bundledRoot);

    /// <summary>Folders searched for a file the user supplied. The working directory always.</summary>
    private readonly IReadOnlyList<string> _lookIn =
        [Path.GetFullPath(cacheRoot), .. (lookIn ?? []).Select(Path.GetFullPath)];

    public async Task<IReadOnlyList<AcquisitionResult>> AcquireAllAsync(
        Recipe recipe,
        IProgress<AcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AcquisitionResult>();

        foreach (var source in recipe.RequiredFiles)
        {
            results.Add(await AcquireAsync(source, progress, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<AcquisitionResult> AcquireAsync(
        RecipeSource source,
        IProgress<AcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cache);

        var target = SafeTargetPath(source.FileName);
        if (target is null)
        {
            return new AcquisitionResult(
                source, AcquisitionStatus.Failed, null, [],
                $"File name in the recipe is not allowed: {source.FileName}");
        }

        // Already there and fine?
        if (File.Exists(target))
        {
            if (Hashing.Equal(Hashing.Sha256File(target), source.Sha256))
            {
                log.Info($"{source.FileName}: already present and verified.");
                return new AcquisitionResult(source, AcquisitionStatus.AlreadyPresent, target, [], null);
            }

            log.Warn($"{source.FileName}: the existing file has the wrong checksum and is discarded.");
            TryDelete(target);
        }

        // Does the launcher ship the file itself? This mostly concerns its own
        // trainer: putting it online just so the launcher can download it again
        // would be a detour with one more thing that can fail.
        //
        // It is still checked against the recipe's checksum. Being shipped is no
        // bonus in trust: the file sits next to a program, in a folder anyone
        // with rights there can write to.
        if (TryTakeBundled(source, target, out var bundledError))
        {
            log.Info($"{source.FileName}: taken from the shipped payload.");
            return new AcquisitionResult(source, AcquisitionStatus.Bundled, target, [], null);
        }

        // Did the user already fetch it? Before any download, because the ones
        // that have to be supplied are not the only large files here - somebody
        // who downloaded a two-gigabyte package by hand should not watch it come
        // down a second time.
        if (TryTakeSupplied(source, target))
        {
            log.Info($"{source.FileName}: found by its checksum among your files and taken.");
            return new AcquisitionResult(source, AcquisitionStatus.Supplied, target, [], null);
        }

        if (source.Urls.Count == 0)
        {
            return new AcquisitionResult(
                source, AcquisitionStatus.NeedsUserAction, null, [],
                bundledError ?? "No source is recorded for this file.");
        }

        var attempts = new List<string>();

        foreach (var url in source.Urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                log.Info($"{source.FileName}: downloading from {url}");
                await DownloadAsync(url, source, target, progress, cancellationToken).ConfigureAwait(false);

                var actual = Hashing.Sha256File(target);
                if (!Hashing.Equal(actual, source.Sha256))
                {
                    TryDelete(target);
                    attempts.Add($"{url}: wrong checksum (expected {Short(source.Sha256)}, got {Short(actual)})");
                    log.Warn($"{source.FileName}: the checksum from {url} does not match.");
                    continue;
                }

                log.Info($"{source.FileName}: downloaded and verified.");
                return new AcquisitionResult(source, AcquisitionStatus.Downloaded, target, attempts, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                attempts.Add($"{url}: {e.Message}");
                log.Warn($"{source.FileName}: {url} failed — {e.Message}");
            }
        }

        return new AcquisitionResult(
            source, AcquisitionStatus.NeedsUserAction, null, attempts,
            "None of the recorded sources delivered a usable file.");
    }

    /// <summary>
    /// Downloads into a .part file and resumes a partial download via Range when
    /// the server supports it. Large is the rule here, not the exception —
    /// downgrade packages run into the gigabytes.
    /// </summary>
    private async Task DownloadAsync(
        string url,
        RecipeSource source,
        string target,
        IProgress<AcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partial = target + ".part";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        // A .part file already bigger than the expected result is junk from an
        // earlier run against a different source.
        if (existing >= source.SizeBytes && source.SizeBytes > 0)
        {
            TryDelete(partial);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        }

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (existing > 0 && !resuming)
        {
            // The server cannot resume — start over, otherwise the data gets mixed up.
            TryDelete(partial);
            existing = 0;
        }

        response.EnsureSuccessStatusCode();

        var declared = response.Content.Headers.ContentLength;
        var total = resuming && declared is not null ? existing + declared : declared;

        // If the server announces a different size than the recipe expects, there
        // is no point pulling gigabytes only to fail the checksum at the end.
        if (source.SizeBytes > 0 && total is not null && total != source.SizeBytes)
        {
            throw new HttpRequestException(
                $"Size mismatch: expected {source.SizeBytes} bytes, announced {total} bytes");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(
                         partial,
                         resuming ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 1 << 16,
                         useAsync: true))
        {
            var buffer = new byte[1 << 16];
            var written = existing;
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                progress?.Report(new AcquisitionProgress(source.FileName, written, total));
            }
        }

        File.Move(partial, target, overwrite: true);
    }

    /// <summary>
    /// The file name comes from the recipe and is therefore foreign data. It may
    /// only be a plain name, never a path, and must never lead out of the working
    /// directory.
    /// </summary>
    private string? SafeTargetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(_cache, fileName));
        var prefix = _cache.EndsWith(Path.DirectorySeparatorChar)
            ? _cache
            : _cache + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Takes a file from the shipped payload into the working directory, provided
    /// one with a matching checksum is there.
    /// </summary>
    /// <param name="error">
    /// Set when a file was there but the wrong one. That is a different case from
    /// "nothing shipped at all" and deserves a different answer — otherwise
    /// somebody goes looking for a file that was there the whole time.
    /// </param>
    /// <summary>
    /// Looks for a file the user downloaded themselves - by checksum, not by name.
    ///
    /// Nexus and MEGA hand out links a program cannot follow, so four recipes ask
    /// the user to fetch the file. Asking them to also put it in a particular
    /// folder under a particular name is one step too many: a browser that has
    /// seen the file before saves it as "... (1).zip", and then a launcher looking
    /// for an exact name says it is missing while it sits right there.
    ///
    /// The checksum was always the thing that decided; the name never protected
    /// anything. So the name stops mattering, and the download folder is searched
    /// as well - the file can stay where it landed.
    ///
    /// Only files of exactly the right size are hashed. That is one cheap look at
    /// a directory entry per file, and nothing else in those folders is read.
    /// </summary>
    private bool TryTakeSupplied(RecipeSource source, string target)
    {
        // Without a size every file in the folder would have to be read through.
        if (source.SizeBytes <= 0)
        {
            return false;
        }

        foreach (var root in _lookIn)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> candidates;

            try
            {
                candidates = Directory.EnumerateFiles(root);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    if (new FileInfo(candidate).Length != source.SizeBytes)
                    {
                        continue;
                    }

                    if (!Hashing.Equal(Hashing.Sha256File(candidate), source.Sha256))
                    {
                        continue;
                    }

                    if (string.Equals(Path.GetFullPath(candidate), target, StringComparison.OrdinalIgnoreCase))
                    {
                        return File.Exists(target);
                    }

                    Directory.CreateDirectory(_cache);
                    File.Copy(candidate, target, overwrite: true);

                    // Copied, not moved: it is the user's file, in the user's
                    // folder, and a program that makes downloads disappear is a
                    // program people stop trusting.
                    return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file being written, or one we may not read. Next.
                }
            }
        }

        return false;
    }

    private bool TryTakeBundled(RecipeSource source, string target, out string? error)
    {
        error = null;

        if (_bundled is null)
        {
            return false;
        }

        // The same protection as for the target: the file name comes from the recipe.
        if (!string.Equals(Path.GetFileName(source.FileName), source.FileName, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = Path.Combine(_bundled, source.FileName);
        if (!File.Exists(candidate))
        {
            return false;
        }

        var actual = Hashing.Sha256File(candidate);
        if (!Hashing.Equal(actual, source.Sha256))
        {
            error = $"The shipped payload contains a {source.FileName}, but with the wrong "
                + $"checksum (expected {Short(source.Sha256)}, found {Short(actual)}). "
                + "It will not be used.";

            log.Warn($"{source.FileName}: the shipped file has the wrong checksum.");
            return false;
        }

        try
        {
            File.Copy(candidate, target, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"The shipped {source.FileName} could not be copied into the "
                + $"working directory: {e.Message}";

            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not deletable: the checksum comparison catches it next time round.
        }
    }

    private static string Short(string? hash) =>
        string.IsNullOrEmpty(hash) ? "?" : hash[..Math.Min(12, hash.Length)];
}
