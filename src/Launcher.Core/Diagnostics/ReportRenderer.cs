using System.Text;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Diagnostics;

/// <summary>
/// Rendert den Diagnosebericht als Klartext: feste Breite, keine Farben, keine
/// Steuerzeichen. So lässt er sich unverändert in ein Forum kopieren — der
/// Bericht ist unser Support-Kanal.
/// </summary>
public static class ReportRenderer
{
    private const int Width = 74;

    public static string Render(DiagnosticReport report)
    {
        var installs = report.Installs;
        var sb = new StringBuilder();

        Rule(sb, '=');
        sb.AppendLine("  Modlauncher IV -- Diagnosebericht");
        sb.AppendLine($"  erstellt {report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Rule(sb, '=');
        sb.AppendLine();

        RenderSystem(sb, report.System);

        if (installs.Count == 0)
        {
            sb.AppendLine("  Keine GTA-IV-Installation gefunden.");
            sb.AppendLine();
            sb.AppendLine("  Gesucht wurde in der Rockstar-Registry, in den Steam-Bibliotheken,");
            sb.AppendLine("  in den Epic-Manifesten und an den üblichen Pfaden.");
            sb.AppendLine();
            sb.AppendLine("  Wenn das Spiel woanders liegt:  mliv detect --path \"D:\\Pfad\\zum\\Spiel\"");
            return sb.ToString();
        }

        if (installs.Count > 1)
        {
            sb.AppendLine($"  {installs.Count} Installationen gefunden.");
            sb.AppendLine();
        }

        for (var i = 0; i < installs.Count; i++)
        {
            RenderInstall(sb, installs[i], installs.Count > 1 ? i + 1 : null);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maschinenweite Randbedingungen stehen vor den Installationen: Smart App
    /// Control entscheidet darüber, ob ein Trainer überhaupt laden darf, und das
    /// soll jemand erfahren, bevor er ein Downgrade fährt.
    /// </summary>
    private static void RenderSystem(StringBuilder sb, SystemEnvironment system)
    {
        Section(sb, "SYSTEM");

        Field(sb, "Betriebssystem", system.OperatingSystem);
        Field(sb, "Rechte", system.IsElevated ? "mit Administratorrechten" : "ohne Administratorrechte");
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

        Field(sb, "Pfad", install.Path);
        Field(sb, "Plattform", Describe(install.Platform));
        Field(sb, "Erkannt über", install.FoundVia);
        sb.AppendLine();

        Field(sb, "Version", $"{install.Version.Raw}  ({install.Version.DisplayName})");
        Field(sb, "Datei", Path.GetFileName(install.ExecutablePath));
        Field(sb, "Größe", $"{install.ExecutableSizeBytes:N0} Bytes");
        Field(sb, "SHA-256", install.ExecutableSha256 ?? "(nicht lesbar)");
        sb.AppendLine();

        Field(sb, "Episodes", DescribeEpisodes(install));
        Field(sb, "Zustand", install.IsVanilla ? "unverändert" : "modifiziert");
        sb.AppendLine();

        if (install.ModArtifacts.Count > 0)
        {
            Section(sb, "GEFUNDENE FREMDDATEIEN");
            foreach (var artifact in install.ModArtifacts)
            {
                var size = artifact.SizeBytes > 0 ? $"{artifact.SizeBytes:N0} B" : "";
                sb.AppendLine($"  {Describe(artifact.Kind),-22}  {artifact.RelativePath,-32} {size}");
            }

            sb.AppendLine();
        }

        Section(sb, "BEFUNDE");
        RenderNotes(sb, install.Notes);

        sb.AppendLine();
        Section(sb, "NÄCHSTER SCHRITT");
        foreach (var line in Wrap(NextStep(install), Width - 4))
        {
            sb.AppendLine($"  {line}");
        }

        sb.AppendLine();
    }

    /// <summary>Die eine Aussage, wegen der jemand den Bericht überhaupt liest.</summary>
    private static string NextStep(GameInstall install)
    {
        if (install.HasBlocker)
        {
            return "Diese Installation kann noch nicht automatisch behandelt werden. "
                 + "Die oben mit [BLOCK] markierten Punkte müssen zuerst geklärt werden.";
        }

        if (!install.IsVanilla)
        {
            return "Die Installation enthält bereits Fremddateien. Der Launcher kann sie nicht "
                 + "zurückrollen, weil er sie nicht installiert hat. Vor einem Downgrade sollte "
                 + "der Ausgangszustand von Hand wiederhergestellt werden.";
        }

        if (install.Version.IsCompleteEdition)
        {
            return "Unveränderte Complete Edition. Für Trainer und die meisten Mods ist ein "
                 + "Downgrade auf 1.0.7.0 nötig. Die Downgrade-Rezepte kommen mit M3.";
        }

        if (install.Version.IsModdingTarget)
        {
            return "Unveränderte Modding-Zielversion. Es fehlt nur der Basis-Stack: "
                 + "GFWL-Stub, ASI-Loader und ScriptHook. Diese Rezepte kommen mit M4.";
        }

        return $"Version {install.Version.Raw} ist bekannt, aber kein Modding-Ziel. "
             + "Ein Downgrade auf 1.0.7.0 oder 1.0.4.0 ist nötig.";
    }

    // ------------------------------------------------------------- Formatierung

    private static string DescribeEpisodes(GameInstall install) => (install.HasTlad, install.HasTbogt) switch
    {
        (true, true) => "TLAD und TBoGT vorhanden",
        (true, false) => "nur TLAD vorhanden",
        (false, true) => "nur TBoGT vorhanden",
        _ => "keine",
    };

    private static string Describe(GamePlatform platform) => platform switch
    {
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Retail / DVD",
        _ => "unbekannt",
    };

    private static string Describe(ModArtifactKind kind) => kind switch
    {
        ModArtifactKind.AsiLoader => "ASI-Loader",
        ModArtifactKind.AsiPlugin => "ASI-Plugin",
        ModArtifactKind.ScriptHook => "ScriptHook",
        ModArtifactKind.ScriptHookDotNet => "ScriptHookDotNet",
        ModArtifactKind.Xlive => "xlive / GFWL-Stub",
        ModArtifactKind.ScriptFolder => "Mod-Ordner",
        _ => "unbekannt",
    };

    private static string Describe(SmartAppControlState state) => state switch
    {
        SmartAppControlState.Enforced => "aktiv — blockiert unsignierte DLLs",
        SmartAppControlState.Evaluation => "Evaluierungsmodus",
        SmartAppControlState.Off => "aus",
        SmartAppControlState.Unknown => "unbekannter Zustand",
        _ => "nicht vorhanden",
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
