using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Core.Acquisition;

public enum AcquisitionStatus
{
    /// <summary>Lag schon im Arbeitsverzeichnis und hatte die richtige Prüfsumme.</summary>
    AlreadyPresent,

    Downloaded,

    /// <summary>
    /// Kam aus dem Lieferumfang des Launchers selbst — nichts geladen, aber
    /// genauso gegen die Prüfsumme im Rezept gemessen wie alles andere.
    /// </summary>
    Bundled,

    /// <summary>Keine Quelle erreichbar — der Nutzer muss die Datei selbst ablegen.</summary>
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
        or AcquisitionStatus.Bundled;
}

public sealed record AcquisitionProgress(string FileName, long BytesRead, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0 ? (int)(BytesRead * 100 / TotalBytes.Value) : null;
}

/// <summary>
/// Beschafft die Dateien, die ein Rezept braucht.
///
/// Grundsatz: eine Datei gilt erst dann als vorhanden, wenn ihre SHA-256 stimmt.
/// Eine vorhandene Datei mit falscher Prüfsumme wird verworfen und neu geladen —
/// sie ist entweder abgebrochen oder ausgetauscht, und beides wollen wir nicht
/// ins Spielverzeichnis kopieren.
///
/// Heruntergeladen wird immer neben das Ziel (.part) und erst nach erfolgreicher
/// Prüfung an seinen Platz verschoben. Ein Abbruch hinterlässt damit nie eine
/// halbe Datei, die beim nächsten Lauf für vollständig gehalten wird.
/// </summary>
/// <param name="bundledRoot">
/// Ordner mit Dateien, die der Launcher selbst mitbringt — etwa den eigenen
/// Trainer. Null, wenn es keinen gibt.
/// </param>
public sealed class SourceAcquirer(HttpClient http, string cacheRoot, IExecutionLog log, string? bundledRoot = null)
{
    private readonly string _cache = Path.GetFullPath(cacheRoot);
    private readonly string? _bundled = bundledRoot is null ? null : Path.GetFullPath(bundledRoot);

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
                $"Unzulässiger Dateiname im Rezept: {source.FileName}");
        }

        // Schon da und in Ordnung?
        if (File.Exists(target))
        {
            if (Hashing.Equal(Hashing.Sha256File(target), source.Sha256))
            {
                log.Info($"{source.FileName}: liegt bereits vor und ist verifiziert.");
                return new AcquisitionResult(source, AcquisitionStatus.AlreadyPresent, target, [], null);
            }

            log.Warn($"{source.FileName}: vorhandene Datei hat die falsche Prüfsumme und wird verworfen.");
            TryDelete(target);
        }

        // Bringt der Launcher die Datei selbst mit? Das betrifft vor allem den
        // eigenen Trainer: ihn im Netz abzulegen, nur damit der eigene Launcher
        // ihn wieder herunterlädt, wäre ein Umweg mit zusätzlicher Fehlerquelle.
        //
        // Geprüft wird trotzdem gegen die Prüfsumme aus dem Rezept. Der
        // Lieferumfang ist kein Vertrauensbonus: die Datei liegt neben einem
        // Programm, in dessen Ordner jeder schreiben kann, der dort Rechte hat.
        if (TryTakeBundled(source, target, out var bundledError))
        {
            log.Info($"{source.FileName}: aus dem Lieferumfang übernommen.");
            return new AcquisitionResult(source, AcquisitionStatus.Bundled, target, [], null);
        }

        if (source.Urls.Count == 0)
        {
            return new AcquisitionResult(
                source, AcquisitionStatus.NeedsUserAction, null, [],
                bundledError ?? "Für diese Datei ist keine Bezugsquelle hinterlegt.");
        }

        var attempts = new List<string>();

        foreach (var url in source.Urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                log.Info($"{source.FileName}: lade von {url}");
                await DownloadAsync(url, source, target, progress, cancellationToken).ConfigureAwait(false);

                var actual = Hashing.Sha256File(target);
                if (!Hashing.Equal(actual, source.Sha256))
                {
                    TryDelete(target);
                    attempts.Add($"{url}: Prüfsumme falsch (erwartet {Short(source.Sha256)}, erhalten {Short(actual)})");
                    log.Warn($"{source.FileName}: Prüfsumme von {url} stimmt nicht.");
                    continue;
                }

                log.Info($"{source.FileName}: geladen und verifiziert.");
                return new AcquisitionResult(source, AcquisitionStatus.Downloaded, target, attempts, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                attempts.Add($"{url}: {e.Message}");
                log.Warn($"{source.FileName}: {url} fehlgeschlagen — {e.Message}");
            }
        }

        return new AcquisitionResult(
            source, AcquisitionStatus.NeedsUserAction, null, attempts,
            "Keine der hinterlegten Quellen hat eine verwendbare Datei geliefert.");
    }

    /// <summary>
    /// Lädt in eine .part-Datei und setzt einen Teildownload per Range fort, wenn
    /// der Server das unterstützt. Groß ist hier die Regel, nicht die Ausnahme —
    /// Downgrade-Pakete gehen in die Gigabyte.
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

        // Eine .part-Datei, die schon größer ist als das erwartete Ergebnis, ist
        // Müll aus einem früheren Lauf gegen eine andere Quelle.
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
            // Server kann nicht fortsetzen — von vorn, sonst entsteht Datensalat.
            TryDelete(partial);
            existing = 0;
        }

        response.EnsureSuccessStatusCode();

        var declared = response.Content.Headers.ContentLength;
        var total = resuming && declared is not null ? existing + declared : declared;

        // Meldet der Server eine andere Größe als das Rezept erwartet, brauchen wir
        // gar nicht erst Gigabyte zu laden, um am Ende an der Prüfsumme zu scheitern.
        if (source.SizeBytes > 0 && total is not null && total != source.SizeBytes)
        {
            throw new HttpRequestException(
                $"Größe weicht ab: erwartet {source.SizeBytes} Bytes, angekündigt {total} Bytes");
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
    /// Der Dateiname stammt aus dem Rezept und ist damit Fremddaten. Er darf nur
    /// ein einfacher Name sein, kein Pfad, und niemals aus dem Arbeitsverzeichnis
    /// herausführen.
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
    /// Holt eine Datei aus dem Lieferumfang ins Arbeitsverzeichnis, sofern dort
    /// eine mit passender Prüfsumme liegt.
    /// </summary>
    /// <param name="error">
    /// Gesetzt, wenn zwar eine Datei da lag, aber die falsche. Das ist ein anderer
    /// Fall als "gar nichts dabei" und verdient eine andere Auskunft — sonst
    /// sucht jemand nach einer Datei, die die ganze Zeit da war.
    /// </param>
    private bool TryTakeBundled(RecipeSource source, string target, out string? error)
    {
        error = null;

        if (_bundled is null)
        {
            return false;
        }

        // Derselbe Schutz wie beim Ziel: der Dateiname kommt aus dem Rezept.
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
            error = $"Im Lieferumfang liegt eine {source.FileName}, aber mit falscher "
                + $"Prüfsumme (erwartet {Short(source.Sha256)}, gefunden {Short(actual)}). "
                + "Sie wird nicht verwendet.";

            log.Warn($"{source.FileName}: mitgelieferte Datei hat die falsche Prüfsumme.");
            return false;
        }

        try
        {
            File.Copy(candidate, target, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"Die mitgelieferte {source.FileName} ließ sich nicht ins "
                + $"Arbeitsverzeichnis kopieren: {e.Message}";

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
            // Nicht löschbar: der Prüfsummenvergleich fängt es beim nächsten Mal ab.
        }
    }

    private static string Short(string? hash) =>
        string.IsNullOrEmpty(hash) ? "?" : hash[..Math.Min(12, hash.Length)];
}
