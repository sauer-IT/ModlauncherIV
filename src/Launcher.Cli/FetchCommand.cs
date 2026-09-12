using ModlauncherIV.Core.Acquisition;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Cli;

internal static class FetchCommand
{
    public static async Task<int> RunAsync(CliOptions options, CatalogLoadResult catalog)
    {
        if (string.IsNullOrWhiteSpace(options.Argument))
        {
            Console.Error.WriteLine("The recipe id is missing. Available recipes: mliv catalog");
            return ExitCode.BadUsage;
        }

        var recipe = catalog.Find(options.Argument);
        if (recipe is null)
        {
            Console.Error.WriteLine($"Recipe not found: {options.Argument}");
            return ExitCode.NothingFound;
        }

        if (recipe.RequiredFiles.Count == 0)
        {
            Console.WriteLine($"{recipe.Id} needs no external files.");
            return ExitCode.Ok;
        }

        var cache = options.CachePath ?? AppPaths.Cache;
        Directory.CreateDirectory(cache);

        Console.WriteLine($"  Working directory   {Path.GetFullPath(cache)}");
        Console.WriteLine($"  Requires            {recipe.RequiredFiles.Count} file(s)");
        Console.WriteLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ModlauncherIV/0.1");

        var log = new ExecutionLog(line => Console.WriteLine($"  {line}"));
        // Auch die CLI kennt den Lieferumfang, damit sich "fetch mliv-trainer"
        // nicht anders verhaelt als derselbe Schritt im Assistenten.
        var acquirer = new SourceAcquirer(http, cache, log, AppPaths.BundledDirectory);
        var progress = new ConsoleProgress();

        var results = await acquirer.AcquireAllAsync(recipe, progress, CancellationToken.None)
            .ConfigureAwait(false);

        progress.Finish();
        Console.WriteLine();

        return Report(results);
    }

    private static int Report(IReadOnlyList<AcquisitionResult> results)
    {
        var missing = results.Where(r => !r.Ok).ToArray();

        Console.WriteLine("  RESULT");
        Console.WriteLine("  --------------");
        foreach (var result in results)
        {
            var status = result.Status switch
            {
                AcquisitionStatus.AlreadyPresent => "already there",
                AcquisitionStatus.Downloaded => "downloaded",
                AcquisitionStatus.Bundled => "shipped",
                AcquisitionStatus.NeedsUserAction => "MISSING",
                _ => "ERROR",
            };

            Console.WriteLine($"  {status,-16} {result.Source.FileName}");
        }

        if (missing.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  All files present and verified.");
            return ExitCode.Ok;
        }

        // The escape hatch: when no source delivers, the user has to know exactly
        // what to put where — with the checksum, or they cannot verify it.
        Console.WriteLine();
        Console.WriteLine("  TO BE SUPPLIED BY HAND");
        Console.WriteLine("  ------------------------");

        foreach (var result in missing)
        {
            Console.WriteLine();
            Console.WriteLine($"  {result.Source.FileName}");
            Console.WriteLine($"      SHA-256  {result.Source.Sha256}");
            Console.WriteLine($"      size     {result.Source.SizeBytes:N0} bytes");

            if (result.Source.Note is not null)
            {
                Console.WriteLine($"      note     {result.Source.Note}");
            }

            foreach (var attempt in result.Attempts)
            {
                Console.WriteLine($"      tried    {attempt}");
            }

            if (result.Error is not null)
            {
                Console.WriteLine($"      {result.Error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Put the file into the working directory and run fetch again.");
        Console.WriteLine("  The checksum is verified — a wrong file gets rejected.");

        return ExitCode.Failed;
    }
}

/// <summary>Progress on a single line, without flooding the output.</summary>
internal sealed class ConsoleProgress : IProgress<AcquisitionProgress>
{
    private string _current = string.Empty;
    private int _lastPercent = -1;
    private bool _wrote;

    public void Report(AcquisitionProgress value)
    {
        if (value.FileName != _current)
        {
            Finish();
            _current = value.FileName;
            _lastPercent = -1;
        }

        var percent = value.Percent ?? -1;
        if (percent == _lastPercent)
        {
            return;
        }

        _lastPercent = percent;
        _wrote = true;

        var shown = percent >= 0
            ? $"{percent,3} %"
            : $"{value.BytesRead / 1024 / 1024,6} MB";

        Console.Write($"\r  {value.FileName,-32} {shown}   ");
    }

    public void Finish()
    {
        if (_wrote)
        {
            Console.WriteLine();
            _wrote = false;
        }
    }
}
