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
public sealed record ExtractArchiveStep(string Archive, string Target = "") : RecipeStep
{
    private string TargetOrRoot => string.IsNullOrWhiteSpace(Target) ? "." : Target;

    public override string Describe() =>
        $"Entpacken: {Archive} -> {(string.IsNullOrWhiteSpace(Target) ? "(Spielwurzel)" : Target)}";

    public override IReadOnlyList<string> AffectedGamePaths(RecipeContext context)
    {
        var archive = context.ResolveSourcePath(Archive);
        if (!File.Exists(archive))
        {
            return [];
        }

        var targetRoot = context.ResolveGamePath(TargetOrRoot);
        var paths = new List<string>();

        try
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                // Verzeichniseinträge enden auf '/' und haben keinen Namen.
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                paths.Add(ResolveEntry(targetRoot, entry.FullName));
            }
        }
        catch (InvalidDataException e)
        {
            throw new RecipeSecurityException($"Archiv nicht lesbar: {Archive} ({e.Message})");
        }

        return paths;
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

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destination = ResolveEntry(targetRoot, entry.FullName);
            var directory = Path.GetDirectoryName(destination);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destination, overwrite: true);
            count++;
        }

        context.Log.Info($"Entpackt: {Archive} ({count} Dateien) -> {TargetOrRoot}");
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
