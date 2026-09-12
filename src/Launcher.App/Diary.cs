using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using ModlauncherIV.Core.Backup;

namespace ModlauncherIV.App;

/// <summary>
/// What the launcher did, written down.
///
/// The handout asks people to send a log when something goes wrong, and until
/// now there was none to send: the trainer wrote one, the launcher wrote
/// nothing at all. What came back instead was "it did not work", which is the
/// least a person can say and the most they can be expected to.
///
/// So: one file, appended to, with what was found, what was fetched, what was
/// written and what failed. It is not a debug trace - it is the answer to "what
/// happened on your machine", in the order it happened.
///
/// Nothing here throws. A launcher that falls over because it could not write
/// its own log would be a bad joke.
/// </summary>
public static class Diary
{
    /// <summary>Above this the file is rolled over. Large enough for several runs.</summary>
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();

    public static string File { get; } = Path.Combine(AppPaths.Root, "launcher.log");

    /// <summary>The one before this. Kept so a crash does not erase its own cause.</summary>
    public static string Previous { get; } = Path.Combine(AppPaths.Root, "launcher.log.1");

    /// <summary>
    /// Opens the log for this run: rolls the file over when it has grown, then
    /// writes who is running, where and with what rights. Those three lines
    /// answer half the questions that ever get asked about a report.
    /// </summary>
    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);

            var info = new FileInfo(File);
            if (info.Exists && info.Length > MaxBytes)
            {
                System.IO.File.Move(File, Previous, overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        Line(string.Empty);
        Line(new string('-', 70));
        Line($"Modlauncher IV {version}   started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Line($"Windows {Environment.OSVersion.Version}   {(IsElevated() ? "as administrator" : "WITHOUT administrator rights")}");
        Line($"Running from {Environment.ProcessPath ?? "?"}");
        Line(new string('-', 70));
    }

    /// <summary>One line, as it comes. Used as the sink of the execution log.</summary>
    public static void Line(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.File.AppendAllText(File, message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or DirectoryNotFoundException or NotSupportedException)
        {
            // Nowhere to write it and nowhere to say so. Carrying on is right:
            // the log is for afterwards, the program is for now.
        }
    }

    public static void Info(string message) => Stamped("INFO ", message);

    public static void Warn(string message) => Stamped("WARN ", message);

    public static void Error(string message) => Stamped("ERROR", message);

    /// <summary>
    /// An exception, with the stack.
    ///
    /// The message alone is what the dialog already showed the user, and it is
    /// rarely enough to find anything - "Object reference not set" has been the
    /// same sentence for twenty years.
    /// </summary>
    public static void Crash(Exception exception, string where)
    {
        Stamped("CRASH", $"{where}: {exception.GetType().Name} - {exception.Message}");

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            Line(inner.StackTrace ?? "   (no stack)");

            if (inner.InnerException is not null)
            {
                Line($"caused by {inner.InnerException.GetType().Name}: {inner.InnerException.Message}");
            }
        }
    }

    /// <summary>Opens the log in whatever reads text here. For the button that sends it.</summary>
    public static void Show()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                Info("Opened before anything was written.");
            }

            Process.Start(new ProcessStartInfo { FileName = File, UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Nothing sensible left to do: the path is in the handout as well.
        }
    }

    private static void Stamped(string level, string message) =>
        Line($"[{DateTimeOffset.Now:HH:mm:ss}] {level} {message}");

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
