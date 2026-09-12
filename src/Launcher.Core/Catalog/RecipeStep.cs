using System.IO.Compression;
using System.Text.Json.Serialization;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.Core.Catalog;

/// <summary>
/// Ein einzelner Arbeitsschritt eines Rezepts.
///
/// Der Vertrag hat drei Teile, und die Trennung ist der Kern des ganzen
/// Sicherheitsmodells:
///
///   <see cref="Describe"/>            — was der Nutzer im Dry-Run liest
///   <see cref="AffectedGamePaths"/>   — welche Dateien angefasst werden, VOR dem Schreiben
///   <see cref="Apply"/>               — die eigentliche Änderung
///
/// Weil ein Schritt seine Ziele nennen kann, ohne sie zu verändern, lässt sich
/// der Snapshot anlegen, bevor irgendetwas passiert. Ein Schritt, der Dateien
/// anfasst, die er nicht angekündigt hat, macht den Rollback unvollständig —
/// deshalb muss jede neue Schrittart beides sauber implementieren.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EnsureDirectoryStep), "ensureDirectory")]
[JsonDerivedType(typeof(CopyFileStep), "copyFile")]
[JsonDerivedType(typeof(DeleteFileStep), "deleteFile")]
[JsonDerivedType(typeof(ExtractArchiveStep), "extractArchive")]
public abstract record RecipeStep
{
    /// <summary>Einzeiler für den Dry-Run.</summary>
    public abstract string Describe();

    /// <summary>
    /// Alle spielrelativen Pfade, die dieser Schritt verändern wird — absolute
    /// Pfade, bereits durch die Pfadprüfung gelaufen.
    /// </summary>
    public abstract IReadOnlyList<string> AffectedGamePaths(RecipeContext context);

    /// <summary>Führt den Schritt aus. Wird nie im Dry-Run aufgerufen.</summary>
    public abstract void Apply(RecipeContext context);

    /// <summary>
    /// Prüft nach dem Ausführen, ob das Ergebnis stimmt. Liefert die Beanstandungen;
    /// leer heißt in Ordnung. "Kopiert" ist nicht dasselbe wie "richtig kopiert".
    /// </summary>
    public virtual IReadOnlyList<string> Verify(RecipeContext context) => [];
}

/// <summary>Legt ein Verzeichnis an, falls es fehlt.</summary>
public sealed record EnsureDirectoryStep(string Target) : RecipeStep
{
    public override string Describe() => $"Verzeichnis anlegen: {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var path = context.ResolveGamePath(Target);
        if (Directory.Exists(path))
        {
            context.Log.Info($"Verzeichnis besteht bereits: {Target}");
            return;
        }

        Directory.CreateDirectory(path);
        context.Log.Info($"Verzeichnis angelegt: {Target}");
    }
}

/// <summary>Kopiert eine beschaffte Datei ins Spielverzeichnis.</summary>
/// <param name="Source">Dateiname im Arbeitsverzeichnis.</param>
/// <param name="Target">Zielpfad, relativ zum Spielverzeichnis.</param>
public sealed record CopyFileStep(string Source, string Target) : RecipeStep
{
    public override string Describe() => $"Kopieren: {Source} -> {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var source = context.ResolveSourcePath(Source);
        var target = context.ResolveGamePath(Target);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"Die Quelldatei fehlt im Arbeitsverzeichnis: {Source}", source);
        }

        var directory = Path.GetDirectoryName(target);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, target, overwrite: true);
        context.Log.Info($"Kopiert: {Source} -> {Target}");
    }

    public override IReadOnlyList<string> Verify(RecipeContext context)
    {
        var source = context.ResolveSourcePath(Source);
        var target = context.ResolveGamePath(Target);

        if (!File.Exists(target))
        {
            return [$"{Target} wurde nicht angelegt."];
        }

        // Byteweise Gleichheit gegen die Quelle: eine abgeschnittene Kopie hat
        // die richtige Existenz, aber den falschen Inhalt.
        if (File.Exists(source) && Hashing.Sha256File(source) != Hashing.Sha256File(target))
        {
            return [$"{Target} stimmt nicht mit der Quelle {Source} überein."];
        }

        return [];
    }
}

/// <summary>Entfernt eine Datei aus dem Spielverzeichnis.</summary>
public sealed record DeleteFileStep(string Target) : RecipeStep
{
    public override string Describe() => $"Löschen: {Target}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context) =>
        [context.ResolveGamePath(Target)];

    public override void Apply(RecipeContext context)
    {
        var path = context.ResolveGamePath(Target);

        if (!File.Exists(path))
        {
            context.Log.Info($"Nicht vorhanden, nichts zu löschen: {Target}");
            return;
        }

        File.Delete(path);
        context.Log.Info($"Gelöscht: {Target}");
    }

    public override IReadOnlyList<string> Verify(RecipeContext context) =>
        File.Exists(context.ResolveGamePath(Target))
            ? [$"{Target} existiert noch."]
            : [];
}

/// <summary>
/// Entpackt ein Archiv ins Spielverzeichnis.
///
/// Für <see cref="AffectedGamePaths"/> muss das Archiv gelesen werden — die
/// betroffenen Dateien stehen erst darin. Fehlt das Archiv noch (Dry-Run vor der
/// Beschaffung), bleibt die Liste leer und der Runner weiß, dass er ohne die
/// Datei nicht ausführen darf.
/// </summary>
/// <param name="Archive">Archivname im Arbeitsverzeichnis.</param>
/// <param name="Target">Zielverzeichnis relativ zum Spiel. Leer = Spielwurzel.</param>
/// <param name="From">
/// Optionaler Teilbaum im Archiv. Nur Eintraege darunter werden entpackt, und
/// zwar ohne dieses Praefix. Notwendig fuer Archive, die alles in einen
/// Wrapper-Ordner legen: ohne das landete "Retail/xyz.dll" als
/// "&lt;Spiel&gt;/Retail/xyz.dll" statt als "&lt;Spiel&gt;/xyz.dll".
/// </param>
public sealed record ExtractArchiveStep(string Archive, string Target = "", string From = "") : RecipeStep
{
    private string TargetOrRoot => string.IsNullOrWhiteSpace(Target) ? "." : Target;

    /// <summary>Das Praefix, normalisiert auf Schrägstriche und mit abschließendem Trenner.</summary>
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
        var target = string.IsNullOrWhiteSpace(Target) ? "(Spielwurzel)" : Target;
        return $"Entpacken: {source} -> {target}";
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
            throw new RecipeSecurityException($"Archiv nicht lesbar: {Archive} ({e.Message})");
        }
    }

    public override void Apply(RecipeContext context)
    {
        var archive = context.ResolveSourcePath(Archive);
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException($"Das Archiv fehlt im Arbeitsverzeichnis: {Archive}", archive);
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
            // Ein Teilbaum, der nichts trifft, ist ein Rezeptfehler und kein
            // stiller Erfolg — sonst meldet das Rezept "fertig" ohne Wirkung.
            throw new FileNotFoundException(
                $"Im Archiv {Archive} liegt nichts unter '{Prefix}'.", archive);
        }

        context.Log.Info($"Entpackt: {Archive} ({count} Dateien) -> {TargetOrRoot}");
    }

    /// <summary>Die Eintraege, die dieser Schritt betrifft, samt Zielpfad ohne Praefix.</summary>
    private IEnumerable<(ZipArchiveEntry Entry, string Relative)> Selected(ZipArchive zip)
    {
        var prefix = Prefix;

        foreach (var entry in zip.Entries)
        {
            // Verzeichniseinträge enden auf '/' und haben keinen Namen.
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
            .Select(p => $"{Path.GetRelativePath(context.GameRoot, p)} fehlt nach dem Entpacken.")
            .ToArray();

        return missing;
    }

    /// <summary>
    /// Archiveinträge sind Fremddaten. "..\..\windows\system32\x.dll" als
    /// Eintragsname ist ein bekannter Angriff (Zip Slip), deshalb wird hier
    /// genauso streng geprüft wie bei Rezeptpfaden.
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
                $"Archiveintrag zeigt aus dem Zielverzeichnis heraus: {entryName}");
        }

        return full;
    }
}
