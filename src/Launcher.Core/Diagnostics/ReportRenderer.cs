using System.Text;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Diagnostics;

/// <summary>
/// Renders the diagnostic report as plain text: fixed width, no colours, no
/// control characters. That way it can be pasted into a forum unchanged — the
/// report is our support channel.
/// </summary>
public static class ReportRenderer
{
    private const int Width = 74;

    public static string Render(DiagnosticReport report)
    {
        var installs = report.Installs;
        var sb = new StringBuilder();

        Rule(sb, '=');
        sb.AppendLine("  Modlauncher IV -- diagnostic report");
        sb.AppendLine($"  created {report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Rule(sb, '=');
        sb.AppendLine();

        RenderSystem(sb, report.System);

        if (installs.Count == 0)
        {
            sb.AppendLine("  No GTA IV installation found.");
            sb.AppendLine();
            sb.AppendLine("  Searched the Rockstar registry, the Steam libraries,");
            sb.AppendLine("  the Epic manifests and the usual paths.");
            sb.AppendLine();
            sb.AppendLine("  If the game lives elsewhere:  mliv detect --path \"D:\\path\\to\\game\"");
            return sb.ToString();
        }

        if (installs.Count > 1)
        {
            sb.AppendLine($"  {installs.Count} installations found.");
            sb.AppendLine();
        }

        for (var i = 0; i < installs.Count; i++)
        {
            RenderInstall(sb, installs[i], installs.Count > 1 ? i + 1 : null);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Machine-wide conditions come before the installations: Smart App Control
    /// decides whether a trainer may load at all, and somebody should learn that
    /// before they run a downgrade.
    /// </summary>
    private static void RenderSystem(StringBuilder sb, SystemEnvironment system)
    {
        Section(sb, "SYSTEM");

        Field(sb, "Operating sys", system.OperatingSystem);
        Field(sb, "Rights", system.IsElevated ? "with administrator rights" : "without administrator rights");
        Field(sb, "Smart App Ctrl", Describe(system.SmartAppControl));
        sb.AppendLine();

        RenderNotes(sb, system.Notes);
        sb.AppendLine();
    }

    private static void RenderNotes(StringBuilder sb, IReadOnlyList<Note> notes)
    {
        foreach (var note in notes.OrderByDescending(n => n.Level))
        {
            sb.AppendLine($"  [{Describe(note.Level)}] {note.Message}");
            if (note.Detail is null)
            {
                continue;
            }

            foreach (var line in Wrap(note.Detail, Width - 12))
            {
                sb.AppendLine($"           {line}");
            }
        }
    }

    private static void RenderInstall(StringBuilder sb, GameInstall install, int? index)
    {
        var heading = index is null ? "INSTALLATION" : $"INSTALLATION {index}";
        Section(sb, heading);

        Field(sb, "Path", install.Path);
        Field(sb, "Platform", Describe(install.Platform));
        Field(sb, "Found via", install.FoundVia);
        sb.AppendLine();

        Field(sb, "Version", $"{install.Version.Raw}  ({install.Version.DisplayName})");
        Field(sb, "File", Path.GetFileName(install.ExecutablePath));
        Field(sb, "Size", $"{install.ExecutableSizeBytes:N0} bytes");
        Field(sb, "SHA-256", install.ExecutableSha256 ?? "(not readable)");
        sb.AppendLine();

        Field(sb, "Episodes", DescribeEpisodes(install));
        Field(sb, "State", install.IsVanilla ? "unchanged" : "modified");
        sb.AppendLine();

        if (install.ModArtifacts.Count > 0)
        {
            Section(sb, "FOREIGN FILES FOUND");
            foreach (var artifact in install.ModArtifacts)
            {
                var size = artifact.SizeBytes > 0 ? $"{artifact.SizeBytes:N0} B" : "";
                sb.AppendLine($"  {Describe(artifact.Kind),-22}  {artifact.RelativePath,-32} {size}");
            }

            sb.AppendLine();
        }

        Section(sb, "FINDINGS");
        RenderNotes(sb, install.Notes);

        sb.AppendLine();
        Section(sb, "NEXT STEP");
        foreach (var line in Wrap(NextStep(install), Width - 4))
        {
            sb.AppendLine($"  {line}");
        }

        sb.AppendLine();
    }

    /// <summary>The one statement somebody reads the whole report for.</summary>
    private static string NextStep(GameInstall install)
    {
        if (install.HasBlocker)
        {
            return "This installation cannot be handled automatically yet. "
                 + "The points marked [BLOCK] above have to be sorted out first.";
        }

        if (!install.IsVanilla)
        {
            return "The installation already contains foreign files. The launcher cannot roll "
                 + "them back because it did not install them. Before a downgrade, the original "
                 + "state should be restored by hand.";
        }

        if (install.Version.IsCompleteEdition)
        {
            return "Unmodified Complete Edition. Trainers and most mods need a downgrade to "
                 + "1.0.7.0 first.";
        }

        if (install.Version.IsModdingTarget)
        {
            return "Unmodified modding target version. Only the base stack is missing: "
                 + "GFWL stub, ASI loader and ScriptHook.";
        }

        return $"Version {install.Version.Raw} is known, but it is not a modding target. "
             + "A downgrade to 1.0.7.0 or 1.0.4.0 is needed.";
    }

    // ------------------------------------------------------------- Formatting

    private static string DescribeEpisodes(GameInstall install) => (install.HasTlad, install.HasTbogt) switch
    {
        (true, true) => "TLAD and TBoGT present",
        (true, false) => "only TLAD present",
        (false, true) => "only TBoGT present",
        _ => "none",
    };

    private static string Describe(GamePlatform platform) => platform switch
    {
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Retail / DVD",
        _ => "unknown",
    };

    private static string Describe(ModArtifactKind kind) => kind switch
    {
        ModArtifactKind.AsiLoader => "ASI loader",
        ModArtifactKind.AsiPlugin => "ASI plugin",
        ModArtifactKind.ScriptHook => "ScriptHook",
        ModArtifactKind.ScriptHookDotNet => "ScriptHookDotNet",
        ModArtifactKind.Xlive => "xlive / GFWL stub",
        ModArtifactKind.ScriptFolder => "mod folder",
        _ => "unknown",
    };

    private static string Describe(SmartAppControlState state) => state switch
    {
        SmartAppControlState.Enforced => "active — blocks unsigned DLLs",
        SmartAppControlState.Evaluation => "evaluation mode",
        SmartAppControlState.Off => "off",
        SmartAppControlState.Unknown => "unrecognised state",
        _ => "not present",
    };

    private static string Describe(NoteLevel level) => level switch
    {
        NoteLevel.Blocker => "BLOCK",
        NoteLevel.Warning => "WARN ",
        _ => "INFO ",
    };

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine($"  {title}");
        sb.AppendLine("  " + new string('-', Math.Min(title.Length + 6, Width - 2)));
    }

    private static void Field(StringBuilder sb, string label, string value)
        => sb.AppendLine($"  {label,-14} {value}");

    private static void Rule(StringBuilder sb, char c)
        => sb.AppendLine(new string(c, Width));

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
