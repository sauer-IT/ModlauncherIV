namespace ModlauncherIV.Core.Execution;

/// <summary>
/// Thrown when a recipe asks for something it is not allowed to ask for.
/// That is not a usage mistake but either an attack or a broken recipe — in
/// both cases nothing gets executed.
/// </summary>
public sealed class RecipeSecurityException(string message) : Exception(message);

/// <summary>Log of one execution run.</summary>
public interface IExecutionLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

/// <summary>Collects the log in memory and optionally passes it on.</summary>
public sealed class ExecutionLog(Action<string>? sink = null) : IExecutionLog
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void Info(string message) => Write("INFO ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {level} {message}";
        _lines.Add(line);
        sink?.Invoke(line);
    }
}

/// <summary>
/// Everything a step needs in order to run.
///
/// The important part is <see cref="ResolveGamePath"/> and
/// <see cref="ResolveSourcePath"/>: every path coming out of a recipe passes
/// through them, and both make sure the result stays inside the directory it is
/// allowed to touch. Without that check a recipe containing
/// <c>..\..\Windows\System32</c> could overwrite arbitrary files.
/// </summary>
public sealed class RecipeContext
{
    public RecipeContext(string gameRoot, string sourceRoot, IExecutionLog log, bool dryRun)
    {
        GameRoot = NormaliseRoot(gameRoot, nameof(gameRoot));
        SourceRoot = NormaliseRoot(sourceRoot, nameof(sourceRoot));
        Log = log;
        DryRun = dryRun;
    }

    /// <summary>The game directory. Target of every writing step.</summary>
    public string GameRoot { get; }

    /// <summary>Working directory holding the acquired files. Only ever read.</summary>
    public string SourceRoot { get; }

    public IExecutionLog Log { get; }

    /// <summary>True when nothing may be written.</summary>
    public bool DryRun { get; }

    /// <summary>Resolves a game-relative path and makes sure it stays inside the game.</summary>
    public string ResolveGamePath(string relative) => Resolve(GameRoot, relative, "game directory");

    /// <summary>Resolves a file name inside the working directory.</summary>
    public string ResolveSourcePath(string relative) => Resolve(SourceRoot, relative, "working directory");

    private static string Resolve(string root, string relative, string label)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new RecipeSecurityException($"Empty path in the recipe ({label}).");
        }

        if (Path.IsPathRooted(relative))
        {
            throw new RecipeSecurityException(
                $"Recipes must not use absolute paths: {relative}");
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RecipeSecurityException($"Unusable path in the recipe: {relative} ({e.Message})");
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeSecurityException(
                $"The recipe points outside the {label}: {relative} -> {full}");
        }

        return full;
    }

    private static string NormaliseRoot(string path, string argument)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", argument);
        }

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }
}
