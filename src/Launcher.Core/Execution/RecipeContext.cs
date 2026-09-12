namespace ModlauncherIV.Core.Execution;

/// <summary>
/// Wird geworfen, wenn ein Rezept etwas verlangt, das es nicht verlangen darf.
/// Das ist kein Bedienfehler, sondern ein Angriffsversuch oder ein kaputtes
/// Rezept — in beiden Fällen wird nichts ausgeführt.
/// </summary>
public sealed class RecipeSecurityException(string message) : Exception(message);

/// <summary>Protokoll eines Ausführungslaufs.</summary>
public interface IExecutionLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

/// <summary>Sammelt das Protokoll im Speicher und reicht es optional weiter.</summary>
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
/// Alles, was ein Schritt zur Ausführung braucht.
///
/// Der wichtigste Teil sind <see cref="ResolveGamePath"/> und
/// <see cref="ResolveSourcePath"/>: jeder Pfad aus einem Rezept läuft durch sie
/// hindurch, und beide stellen sicher, dass das Ergebnis innerhalb des jeweils
/// erlaubten Verzeichnisses liegt. Ohne diese Prüfung könnte ein Rezept mit
/// <c>..\..\Windows\System32</c> beliebige Dateien überschreiben.
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

    /// <summary>Das Spielverzeichnis. Ziel aller schreibenden Schritte.</summary>
    public string GameRoot { get; }

    /// <summary>Arbeitsverzeichnis mit den beschafften Dateien. Wird nur gelesen.</summary>
    public string SourceRoot { get; }

    public IExecutionLog Log { get; }

    /// <summary>True, wenn nichts geschrieben werden darf.</summary>
    public bool DryRun { get; }

    /// <summary>Löst einen spielrelativen Pfad auf und stellt sicher, dass er im Spiel liegt.</summary>
    public string ResolveGamePath(string relative) => Resolve(GameRoot, relative, "Spielverzeichnis");

    /// <summary>Löst einen Dateinamen im Arbeitsverzeichnis auf.</summary>
    public string ResolveSourcePath(string relative) => Resolve(SourceRoot, relative, "Arbeitsverzeichnis");

    private static string Resolve(string root, string relative, string label)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new RecipeSecurityException($"Leerer Pfad im Rezept ({label}).");
        }

        if (Path.IsPathRooted(relative))
        {
            throw new RecipeSecurityException(
                $"Rezepte dürfen keine absoluten Pfade verwenden: {relative}");
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RecipeSecurityException($"Unbrauchbarer Pfad im Rezept: {relative} ({e.Message})");
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeSecurityException(
                $"Das Rezept zeigt aus dem {label} heraus: {relative} -> {full}");
        }

        return full;
    }

    private static string NormaliseRoot(string path, string argument)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pfad darf nicht leer sein.", argument);
        }

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }
}
