# Modlauncher IV

Ein geführter Downgrader und Mod-Installer für GTA IV — und ein selbstgebauter
Trainer, den er am Ende ausliefert.

**Stand: M3 (Maschinerie)** — Rezept-Engine mit Snapshot und Rollback, Beschaffung mit
Hash-Prüfung und Mirror-Kette, signierter Katalog.
Einziger Befehl, der das Spiel verändert, ist `apply` — nach Rückfrage und mit
vorherigem Snapshot.

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
fetch  <rezept-id>     benötigte Dateien laden und per SHA-256 prüfen
apply  <rezept-id>     Rezept ausführen, nach Rückfrage und mit Snapshot
status                 was der Launcher an dieser Installation verändert hat
route  [version]       welcher Weg zu einer anderen Spielversion führt
guard                  ob die Plattform das Spiel zurückpatchen kann
verify                 ob noch alles so liegt, wie der Launcher es einbaute

catalog-key            Signierschlüsselpaar erzeugen
catalog-sign           Katalog indizieren und signieren
```

Rückgabewerte: `0` erfolgreich · `1` nichts gefunden · `2` falscher Aufruf ·
`3` Blocker gefunden, nichts ausgeführt · `4` Ausgabe nicht schreibbar ·
`5` Ausführung fehlgeschlagen.

## Tests

```
.\tests\run-tests.ps1
```

85 Tests gegen gefälschte Spielverzeichnisse. Keine echte Installation wird
angefasst. Abgedeckt sind unter anderem:

- Prüfsummenschutz und Pfadausbruch aus dem Spielverzeichnis
- dass der Dry-Run wirklich nichts verändert
- Rollback nach einem Fehlschlag mitten im Rezept
- Download über eine Mirror-Kette gegen einen lokalen HTTP-Server: erste Quelle
  404, zweite liefert falschen Inhalt, dritte ist korrekt
- Versionsgraph: Wegsuche über mehrere Downgrade-Kanten hinweg
- Update-Sperre: offene Steam-Installation erkennen, Schalter setzen, Sicherung anlegen
- Gegenprobe: veränderte und gelöschte Dateien werden dem Rezept zugeordnet
- Katalogsignatur: unsigniert wird abgelehnt, nachträglich veränderte
  Rezeptdatei fällt auf, gefälschte Signatur wird erkannt

**Das Skript ist bewusst reines ASCII.** PowerShell 5.1 liest `.ps1` ohne BOM als
CP1252; ein UTF-8-Geviertstrich wird dabei unter anderem zu `”`, und das gilt
als String-Begrenzer. Der Parser verrutscht dann still ab dieser Stelle.

## Katalogsignatur

Der Katalog bestimmt, welche Dateien ins Spielverzeichnis geschrieben werden.
Wer ihn austauschen kann, kann beliebigen Code unterschieben — TLS schützt dabei
nur den Transportweg, nicht vor einem übernommenen Server.

Deshalb: ein signierter `index.json` führt jede Rezeptdatei mit ihrer Prüfsumme
auf, die Signatur wird gegen einen fest eingebauten öffentlichen Schlüssel
geprüft (ECDSA P-256, SHA-256). Ohne gültige Signatur wird **kein einziges**
Rezept geladen — nicht "die unauffälligen trotzdem", denn wer fälschen kann,
sucht sich aus, welche unauffällig aussehen.

```
mliv catalog-key  --key C:\keys\catalog.pem        einmalig, ausserhalb des Repos
mliv catalog-sign --catalog .\catalog --key C:\keys\catalog.pem
```

Der öffentliche Teil gehört in `CatalogSignature.EmbeddedPublicKey`, der private
**nicht ins Repository**. Solange dort kein Schlüssel steht, lehnt der Launcher
jeden Katalog ab; für die Entwicklung gibt es `--allow-unsigned`, was laut warnt,
und `--public-key <Base64>` für einen abweichenden Signierer.

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
- **M1** Rezept-Engine, Snapshot, Rollback, Ledger, Dry-Run ✔
- **M2** Beschaffung, Hash-Prüfung, Mirror, Katalogsignatur ✔
- **M3** Versionsgraph, Update-Sperre, Gegenprobe ✔ · Downgrade-Rezepte offen ← *hier*
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
