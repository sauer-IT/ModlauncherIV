# Modlauncher IV

Ein geführter Downgrader und Mod-Installer für GTA IV — und ein selbstgebauter
Trainer, den er am Ende ausliefert.

**Stand: M3 abgeschlossen** — Rezept-Engine mit Snapshot, Rollback und Rückbau,
Beschaffung mit Hash-Prüfung und Mirror-Kette, signierter Katalog. Der Downgrade
ist an einer echten Installation gelaufen: 1.2.0.59 → 1.0.7.0, das Spiel startet.
Einziger Befehl, der das Spiel verändert, ist `apply` — nach Rückfrage und mit
vorherigem Snapshot.

## Aufbau

| Projekt | Zweck |
|---|---|
| `src/Launcher.Core` | Domäne und Pipeline. Keine UI-Abhängigkeit, damit gegen Fixtures testbar. |
| `src/Launcher.Cli` | Headless-Frontend (`mliv`). Dry-Runs, Diagnose, CI. |
| `src/Launcher.App` | WPF-Wizard. Kommt mit M5. |
| `src/Trainer` | C++ ASI-Plugin, x86. Kommt ab M4. |
| `catalog/` | Die deklarativen Rezepte. Fünf Stück, vier davon erprobt. |
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
remove <rezept-id>     Rezept zurückbauen. --all für alles, neueste zuerst
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

100 Tests gegen gefälschte Spielverzeichnisse. Keine echte Installation wird
angefasst. Abgedeckt sind unter anderem:

- Prüfsummenschutz und Pfadausbruch aus dem Spielverzeichnis
- dass der Dry-Run wirklich nichts verändert
- Rollback nach einem Fehlschlag mitten im Rezept
- Download über eine Mirror-Kette gegen einen lokalen HTTP-Server: erste Quelle
  404, zweite liefert falschen Inhalt, dritte ist korrekt
- Versionsgraph: Wegsuche über mehrere Downgrade-Kanten hinweg
- Update-Sperre: offene Steam-Installation erkennen, Schalter setzen, Sicherung anlegen
- Gegenprobe: veränderte und gelöschte Dateien werden dem Rezept zugeordnet
- Rückbau: neu angelegte Dateien verschwinden, überschriebene bekommen ihren
  alten Inhalt zurück, gebundene Rezepte werden nicht entfernt
- Katalogsignatur: unsigniert wird abgelehnt, nachträglich veränderte
  Rezeptdatei fällt auf, gefälschte Signatur wird erkannt

**Das Skript ist bewusst reines ASCII.** PowerShell 5.1 liest `.ps1` ohne BOM als
CP1252; ein UTF-8-Geviertstrich wird dabei unter anderem zu `”`, und das gilt
als String-Begrenzer. Der Parser verrutscht dann still ab dieser Stelle.

## Der Katalog — Stand und Vorbehalte

Fünf Rezepte mit **echten, selbst gebildeten SHA-256-Prüfsummen**. Vier davon
sind **an einer echten Installation gelaufen** — Complete Edition 1.2.0.59 über
den Rockstar Games Launcher, heruntergestuft auf 1.0.7.0. Das Spiel startet.

| Rezept | Quelle | Größe | Stand |
|---|---|---|---|
| `downgrade-ce-1070` | GitHub-Release des Gillian-Guide-Projekts | 111 MB | gelaufen |
| `ultimate-asi-loader` | ThirteenAG, GitHub-Release | 928 KB | gelaufen |
| `gfwl-stub` | FusionFix Legacy Addon, GitHub-Release | 4,0 MB | gelaufen |
| `vc80-runtime` | vom Nutzer beigestellt | 7,1 MB | gelaufen |
| `scripthook-dotnet` | ClonkAndre, GitHub-Release | 647 KB | ungetestet |

**Ohne `vc80-runtime` startet nichts.** 1.0.7.0 wurde gegen die
Visual-C++-2005-Laufzeit gebaut; auf heutigen Systemen fehlt sie, und das
Downgrade-Paket bringt sie nicht mit. Windows meldet dann nur „Die
Side-by-Side-Konfiguration ist ungültig", was den eigentlichen Grund nicht
verrät. Die Laufzeit wird neben die EXE gelegt statt systemweit installiert —
das bleibt im Spielverzeichnis, ist umkehrbar, und es braucht keinen
Rezeptschritt, der fremde Installer ausführen darf.

**Warum nicht der übliche Downgrader.** Der verbreitete GTAIVDowngrader (v2.2,
Januar 2025) holt seine Spielpakete aus einer Dropbox. Diese Links liefern
inzwischen nur noch eine „File Deleted"-Seite — für `1040.zip`, `1070.zip` und
`1080.zip` gleichermaßen, in beiden Zweigen seines Manifests. Der übliche Weg ist
damit derzeit kaputt. Gillians Downgrader nutzt stattdessen GitHub-Releases, und
von dort stammt unser Paket.

Nebenbei ein Beleg, dass die Größenprüfung aus M2 ihren Zweck erfüllt: die
Dropbox-Antwort war 185 KB statt 85,7 MB und wäre abgewiesen worden, bevor
irgendetwas das Spielverzeichnis erreicht.

**Was der echte Lauf ergeben hat:**

- 187 Dateien ersetzt. Liegen bleiben nur Rockstar-Launcher-Artefakte
  (`MTLX.dll`, `index.bin`, `metadata.dat`, `title.rgl`, `uninstall.exe`) und
  keine Spieldaten — das 1.0.7.0-Spiel liest sie nicht. Ein Aufräumschritt ist
  damit nicht nötig; `uninstall.exe` zu entfernen wäre sogar schädlich.
- Die `GTAIV.exe` der Complete Edition (MD5 `1a47b45f…`) steht in **keiner** der
  bekannten Hash-Listen für 1.2.0.59 — die Dateien stammen vom August 2026, die
  Datenbasis der Community von Januar 2025. Gestört hat es nicht.
- Die resultierende 1.0.7.0-`GTAIV.exe` (`ab21c0d9…cb4c`) ist byte-identisch mit
  der aus einem unabhängig bezogenen `Retail-1070.zip`. Zwei Quellen, dieselbe
  Prüfsumme.

**Nach dem Downgrade nicht über den Rockstar Games Launcher starten**, sondern
direkt über `GTAIV.exe` — sonst bemerkt der Launcher die veränderte Installation.

## Trainer

```
.\scripts\build-trainer.ps1 -Deploy
```

Baut `src/Trainer` zu `ModlauncherIV-Trainer.asi` und legt es in
`<Spiel>\plugins\`. Zwei Randbedingungen sind keine Bequemlichkeit, sondern
Voraussetzung:

- **x86.** GTA IV ist 32-bit. Eine x64-DLL wird vom ASI-Loader kommentarlos
  ignoriert — der Fehler äußert sich als „nichts passiert".
- **Statische C-Laufzeit (`/MT`).** Ein Trainer, der eine Redistributable
  voraussetzt, wäre ausgerechnet hier fehl am Platz: an genau einer fehlenden
  Visual-C++-Laufzeit ist das Spiel nach dem Downgrade zuerst gescheitert.

**Stufe T0** lädt, meldet sich im Logfile und tut sonst nichts. Das ist der
ganze Zweck: ohne echtes Laden lässt sich nicht beantworten, ob der ASI-Loader
das Plugin annimmt und ob Smart App Control eine unsignierte DLL in
`GTAIV.exe` zulässt.

Der `VersionAdapter` prüft beim Laden die Spielversion und **bricht ab, wenn
sie nicht unterstützt wird**. Auf einer anderen Version stimmen Native-Hashes
und Speicheradressen nicht, und Schreiben an falschen Adressen fällt nicht
sofort auf, sondern später und an ganz anderer Stelle.

Gearbeitet wird nicht in `DllMain`, sondern in einem eigenen Thread — dort hält
Windows die Loader-Sperre, und wer mehr tut als das Nötigste riskiert einen
Deadlock, der sich als „hängt beim Spielstart" äußert.

Das Logfile liegt als `ModlauncherIV-Trainer.log` neben der DLL und wird nach
jeder Zeile geleert; sonst fehlt nach einem Absturz genau die Zeile, die den
Grund verraten hätte.

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

**Für den Trainer beantwortet (T0):** Ein unsigniertes — genauer: selbstsigniertes —
ASI wurde bei aktivem Smart App Control (`State = 1`) anstandslos in `GTAIV.exe`
geladen, ohne eine einzige Code-Integrity-Meldung. Das ist ein Datenpunkt, keine
Garantie: SAC entscheidet reputationsbasiert, und ob die Selbstsignatur dabei
etwas beigetragen hat, lässt sich weiterhin nicht messen.

**Alter Stand, überholt:** Für den Trainer hilft das Single-File-Verfahren
nicht. Eine `.asi` ist definitionsgemäß eine unsignierte DLL, die in `GTAIV.exe`
geladen wird — genau der Vorgang, den SAC unterbindet. Betrifft auch jeden
Endnutzer mit aktivem Smart App Control.

## Meilensteine

- **M0** Erkennung und Diagnosebericht ✔
- **M1** Rezept-Engine, Snapshot, Rollback, Ledger, Dry-Run ✔
- **M2** Beschaffung, Hash-Prüfung, Mirror, Katalogsignatur ✔
- **M3** Downgrade-Rezepte, Versionsgraph, Update-Sperre, Gegenprobe, Rückbau ✔
- **M4** Trainer T0 ✔ · Basis-Stack · T1 Menügerüst ← *hier*
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
