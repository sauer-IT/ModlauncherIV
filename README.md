# Modlauncher IV

Ein geführter Downgrader und Mod-Installer für GTA IV — und ein selbstgebauter
Trainer, den er am Ende ausliefert.

**Stand: M1** — Rezept-Engine mit Snapshot, Rollback, Ledger und Dry-Run.
Einziger schreibender Befehl ist `apply`, und der legt vorher eine Sicherung an.

## Aufbau

| Projekt | Zweck |
|---|---|
| `src/Launcher.Core` | Domäne und Pipeline. Keine UI-Abhängigkeit, damit gegen Fixtures testbar. |
| `src/Launcher.Cli` | Headless-Frontend (`mliv`). Dry-Runs, Diagnose, CI. |
| `src/Launcher.App` | WPF-Wizard. Kommt mit M5. |
| `src/Trainer` | C++ ASI-Plugin, x86. Kommt ab M4. |
| `catalog/` | Die deklarativen Rezepte. Inhalte ab M3. |
| `tests/` | Fixtures und Testskript. |

## Voraussetzungen

- **.NET 10 SDK** — für Launcher.Core und Launcher.Cli
- **Visual Studio Build Tools mit C++ (x86)** — erst ab M4 für den Trainer

## Bauen und ausführen

```
.\scripts\run.ps1 detect
```

Befehle:

```
detect                 Installationen suchen, Diagnosebericht ausgeben
catalog                verfügbare Rezepte auflisten
plan   <rezept-id>     zeigen, was ein Rezept tun würde — ändert nichts
apply  <rezept-id>     Rezept ausführen, nach Rückfrage und mit Snapshot
status                 was der Launcher an dieser Installation verändert hat
```

Rückgabewerte: `0` erfolgreich · `1` nichts gefunden · `2` falscher Aufruf ·
`3` Blocker gefunden, nichts ausgeführt · `4` Ausgabe nicht schreibbar ·
`5` Ausführung fehlgeschlagen.

## Tests

```
.\tests\run-tests.ps1
```

Baut Fixtures — gefälschte Spielverzeichnisse — und prüft die Pipeline gegen
sie. Keine echte Installation wird angefasst. Abgedeckt sind unter anderem:
Prüfsummenschutz, Pfadausbruch aus dem Spielverzeichnis, dass der Dry-Run
wirklich nichts verändert, und der Rollback nach einem Fehlschlag mitten im
Rezept.

## Smart App Control

Auf diesem Entwicklungsrechner ist Smart App Control aktiv. Es hat den ersten
Build sofort blockiert: ein normaler `dotnet build` erzeugt `mliv.exe` plus
`mliv.dll`, die exe darf starten, aber das Laden der unsignierten `mliv.dll`
lehnt die Code-Integrity-Richtlinie ab.

```
Ereignis 3077 — attempted to load mliv.dll that did not meet the
Enterprise signing level requirements
Policy ID {0283ac0f-fff1-49ae-ada1-8a933130cad6}
```

**Was gemessen wurde — und was nicht.** Als der Block aktiv war, lief ein
Single-File-Publish zuverlässig durch: ohne separate Managed-DLL gibt es nichts
zu blockieren. Später hörte SAC von sich aus auf, auch unsignierte Builds zu
blockieren. Drei Wiederholungen mit erzwungenem Neukompilieren liefen alle
durch, signiert wie unsigniert.

Daraus folgt das eigentliche Problem: **SAC ist nicht regelhaft, sondern
reputationsbasiert.** Microsofts Intelligent Security Graph entscheidet pro
Datei, und dieselbe Datei kann heute blockiert und morgen zugelassen werden. Ob
das Signieren geholfen hat, ließ sich deshalb nicht sauber messen.

**Was daraus gebaut wurde:**

| Maßnahme | Wirkung |
|---|---|
| Single-File-Publish (`scripts/run.ps1`) | Entfernt die separate Managed-DLL — die eine Angriffsfläche, bei der der Block nachweislich griff. |
| Signieren bei jedem Durchlauf (`scripts/sign.ps1`) | Die Release-Pipeline steht von Anfang an; später wird nur das Zertifikat getauscht. |
| SAC-Erkennung im Diagnosebericht | Der Nutzer erfährt es **vor** dem Downgrade, nicht wenn der Trainer stumm bleibt. |

**Kein Selbstbetrug beim Signieren:** ein selbstsigniertes Zertifikat stellt SAC
nicht zufrieden. Bewertet wird der Ruf des Signierers beim ISG, nicht die lokale
Vertrauenskette. Deterministisch löst das nur ein echtes Codesigning-Zertifikat,
dessen Reputation aufgebaut ist — `MLIV_SIGN_THUMBPRINT` setzen, dann greift der
Release-Pfad in `sign.ps1`.

**Weiterhin offen für M4:** Für den Trainer hilft das Single-File-Verfahren
nicht. Eine `.asi` ist definitionsgemäß eine unsignierte DLL, die in `GTAIV.exe`
geladen wird — genau der Vorgang, den SAC unterbindet. Betrifft auch jeden
Endnutzer mit aktivem Smart App Control.

## Meilensteine

- **M0** Erkennung und Diagnosebericht ✔
- **M1** Rezept-Engine, Snapshot, Rollback, Ledger, Dry-Run ← *hier*
- **M2** Beschaffung: Download, Hash, Mirror, manueller Fallback
- **M3** Downgrade-Rezepte und Update-Sperre
- **M4** Basis-Stack (ASI-Loader, xliveless, ScriptHook) · Trainer T0
- **M5** WPF-Wizard und Dev-Modus · Trainer T1
- **M6** Profile, Deinstallation, Katalog-Update · Trainer T2/T3

Der vollständige Projektplan mit Architektur, Risiken und offenen Fragen liegt
als eigenes Dokument vor.

## Grundregeln

- Der Kern fasst nie direkt Dateien an — jede Änderung läuft durch die Pipeline
  aus Pre-Flight, Snapshot, Apply, Verify, Commit, mit automatischem Rollback.
- Ledger und Snapshots liegen unter `%LOCALAPPDATA%\ModlauncherIV\`, nicht im
  Spielverzeichnis.
- Es werden keine Spieldateien und keine fremden Mods mit ausgeliefert. Alles
  wird von der Originalquelle geladen und per SHA-256 geprüft.
