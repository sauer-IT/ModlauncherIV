# Modlauncher IV

Ein geführter Downgrader und Mod-Installer für GTA IV — und ein selbstgebauter
Trainer, den er am Ende ausliefert.

**Stand: M5 abgeschlossen, Trainer bis T2c** — der Assistent führt vom
unveränderten Spiel bis zum laufenden Trainer durch, ohne dass man eine
Befehlszeile anfassen muss.

Der vollständige Weg ist an einer echten Installation gelaufen: Complete Edition
1.2.0.59 über den Rockstar Games Launcher, heruntergestuft auf 1.0.7.0, dazu
ASI-Loader, GFWL-Stub, Visual-C++-2005-Laufzeit und der selbstgebaute Trainer —
197 Dateien unter Verwaltung, alle per Gegenprobe unverändert.

Einziges, was das Spiel verändert, ist der Schritt „Einbauen" im Assistenten
beziehungsweise `apply` in der CLI — beides nach Rückfrage und mit vorherigem
Snapshot.

## Aufbau

| Projekt | Zweck |
|---|---|
| `src/Launcher.Core` | Domäne, Pipeline und Wegplanung. Keine UI-Abhängigkeit, damit gegen Fixtures testbar. |
| `src/Launcher.Cli` | Headless-Frontend (`mliv`). Dry-Runs, Diagnose, CI. |
| `src/Launcher.App` | Der Assistent (WPF). Das Programm, das man startet. |
| `src/Trainer` | C++ ASI-Plugin, x86, IV-SDK. Spieler, Waffen, Fahrzeuge, Welt (T2c). |
| `catalog/` | Die deklarativen Rezepte. Sechs Stück, fünf davon erprobt. |
| `tests/` | Fixtures und Testskript. |

Die **Wegplanung** (`Core/Planning`) ist der Unterschied zwischen der CLI und
dem Assistenten. Die CLI führt ein Rezept aus, das man ihr nennt. Der Planer
leitet aus „ich will den Trainer" selbst ab, dass davor ein Downgrade, ein
ASI-Loader und eine Laufzeit stehen — und überspringt, was schon installiert ist.

Der Punkt, an dem es sich entscheidet: Rezepte werden gegen die Version geprüft,
die das Spiel **nach** dem Versionswechsel hat, nicht gegen die aktuelle. Sonst
wäre jedes 1.0.7.0-Rezept für jeden Nutzer auf der Complete Edition „ungeeignet"
— obwohl es genau das ist, was er am Ende haben will. Ein Rezept ist dort nicht
unpassend, sondern noch nicht an der Reihe.

Der Versionswechsel steht deshalb immer vorn: ein Downgrade tauscht hunderte
Dateien, alles vorher Eingebaute wäre danach überschrieben oder halb
überschrieben.

Die Logik liegt in Core und nicht im Fenster, weil sich ein Fenster nicht gegen
Fixtures testen lässt — und weil genau hier die Fehler liegen, die ein Nutzer als
„der Launcher hat mir das Spiel zerlegt" erlebt. Zu erreichen ist sie ohne
Oberfläche über `mliv journey`.

## Voraussetzungen

- **.NET 10 SDK** — für Launcher.Core und Launcher.Cli
- **Visual Studio Build Tools mit C++ (x86)** — für den Trainer. SDK und
  D3DX-Header holt `build-trainer.ps1` selbst und prüft sie per SHA-256.

## Bauen und ausführen

```
dotnet publish src\Launcher.App -c Release -o artifacts\app
.\artifacts\app\ModlauncherIV.exe      der Assistent

.\scripts\run.ps1 detect               die CLI aufrufen
.\scripts\play.ps1                     GTA IV starten, am Rockstar-Launcher vorbei
.\scripts\pack-trainer.ps1             Trainer bauen und im Katalog installierbar machen
.\scripts\build-trainer.ps1 -Deploy    Trainer bauen und von Hand ins Spiel legen
```

Der Assistent verlangt **Administratorrechte**, und zwar von vornherein. GTA IV
liegt bei Steam wie bei Rockstar unter `C:\Program Files`; dorthin schreiben
heißt erhöhte Rechte, und das gilt auch für den Snapshot, ohne den es keinen
Rückbau gibt. Sich erst beim Schreiben neu zu starten hieße: Assistent von vorn,
Auswahl weg, und im schlimmsten Fall eine Erhöhung mitten in einer Transaktion.

Er nimmt Katalog und Lieferumfang aus seinem **eigenen Ordner**, nicht aus dem
Arbeitsverzeichnis — ein über eine Verknüpfung gestartetes Programm hat ein
beliebiges, und fände dann seine eigenen Rezepte nicht.

**Der Trainer startet nicht separat.** Er liegt als
`plugins\ModlauncherIV-Trainer.asi` im Spielverzeichnis und wird vom ASI-Loader
beim Spielstart mitgeladen. Im Spiel öffnet `F7` das Menü.

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
journey <id,id,...>    voller Weg zum Wunschzustand, mit Abhängigkeiten
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

131 Tests gegen gefälschte Spielverzeichnisse. Keine echte Installation wird
angefasst. Läuft GTA IV gerade, bricht das Skript vorn ab — sonst blockiert jeder
Pre-Flight zu Recht, und vier Tests schlagen fehl, ohne dass am Code etwas falsch
wäre. Abgedeckt sind unter anderem:

- Prüfsummenschutz und Pfadausbruch aus dem Spielverzeichnis
- dass der Dry-Run wirklich nichts verändert
- Rollback nach einem Fehlschlag mitten im Rezept
- Download über eine Mirror-Kette gegen einen lokalen HTTP-Server: erste Quelle
  404, zweite liefert falschen Inhalt, dritte ist korrekt
- Versionsgraph: Wegsuche über mehrere Downgrade-Kanten hinweg
- Wegplanung: dass ein Rezept für 1.0.7.0 auf einer Complete Edition angenommen
  wird, sobald das Downgrade im selben Weg davor steht — und abgelehnt, wenn
  nicht. Dazu Ringabhängigkeiten, Konflikte und unbekannte Ausgangsversionen
- Lieferumfang: dass eine mitgelieferte Datei mit falscher Prüfsumme abgelehnt
  wird und nicht einmal ins Arbeitsverzeichnis gelangt
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

Sechs Rezepte mit **echten, selbst gebildeten SHA-256-Prüfsummen**. Fünf davon
sind **an einer echten Installation gelaufen** — Complete Edition 1.2.0.59 über
den Rockstar Games Launcher, heruntergestuft auf 1.0.7.0. Das Spiel startet.

| Rezept | Quelle | Größe | Stand |
|---|---|---|---|
| `downgrade-ce-1070` | GitHub-Release des Gillian-Guide-Projekts | 111 MB | gelaufen |
| `ultimate-asi-loader` | ThirteenAG, GitHub-Release | 928 KB | gelaufen |
| `gfwl-stub` | FusionFix Legacy Addon, GitHub-Release | 4,0 MB | gelaufen |
| `vc80-runtime` | vom Nutzer beigestellt | 7,1 MB | gelaufen |
| `mliv-trainer` | mitgeliefert, selbst gebaut | 205 KB | gelaufen |
| `scripthook-dotnet` | ClonkAndre, GitHub-Release | 647 KB | ungetestet |

**Der Trainer wird mitgeliefert, nicht heruntergeladen.** Er ist die einzige
Datei im Katalog, die dieses Projekt selbst herstellt; ihn irgendwo abzulegen,
damit der eigene Launcher ihn wieder holt, wäre ein Umweg mit einer
zusätzlichen Fehlerquelle. Er liegt in `bundled\` neben dem Programm und wird
von dort übernommen.

Geprüft wird er trotzdem gegen die Prüfsumme im Rezept. **Der Lieferumfang ist
kein Vertrauensbonus:** der Ordner liegt neben einem Programm, in den jeder
schreiben kann, der dort Rechte hat.

Sein Rezept wird **erzeugt, nicht gepflegt**. Prüfsumme und Größe ändern sich
bei jedem Build — ein von Hand geschriebenes Rezept lehnte nach dem nächsten
Build genau die Datei ab, die es installieren soll. `scripts\pack-trainer.ps1`
baut, misst, schreibt das Rezept und signiert den Katalog neu. Ohne den letzten
Schritt lädt der Launcher anschließend gar nichts mehr, und zwar zu Recht: eine
Rezeptdatei, die nicht zum signierten Index passt, ist aus seiner Sicht nicht
von einer manipulierten zu unterscheiden.

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
.\scripts\pack-trainer.ps1             bauen und im Katalog installierbar machen
.\scripts\build-trainer.ps1 -Deploy    bauen und von Hand ins Spiel legen
```

Der übliche Weg ist der erste: danach steht der Trainer im Assistenten zur
Auswahl und wird wie jede andere Mod installiert — mit Snapshot, Ledger und
Rückbau. Der zweite Weg bleibt für die Entwicklung, wenn man nur schnell eine
Änderung im Spiel sehen will.

Gebaut wird `src/Trainer` zu `ModlauncherIV-Trainer.asi`. Zwei Randbedingungen
sind keine Bequemlichkeit, sondern Voraussetzung:

- **x86.** GTA IV ist 32-bit. Eine x64-DLL wird vom ASI-Loader kommentarlos
  ignoriert — der Fehler äußert sich als „nichts passiert".
- **Statische C-Laufzeit (`/MT`).** Ein Trainer, der eine Redistributable
  voraussetzt, wäre ausgerechnet hier fehl am Platz: an genau einer fehlenden
  Visual-C++-Laufzeit ist das Spiel nach dem Downgrade zuerst gescheitert.

**Das Menü läuft im Spiel** — F7 öffnet, Numblock oder Pfeiltasten
bedienen, Schalter und Auswahl reagieren, Aktionen laufen bis ins Logfile durch.

| Taste | Wirkung |
|---|---|
| `F7` | Menü öffnen und schließen |
| `Num 8` / `↑` · `Num 2` / `↓` | Auswahl bewegen |
| `Num 4` / `←` · `Num 6` / `→` | Wert ändern |
| `Num 5` / `Enter` | Auswählen |
| `Num 0` / `Rücktaste` | Zurück |

Die **Menülogik kennt das Spiel nicht** — Struktur, Navigation und Zustand
liegen in `menu/`, gezeichnet wird über `IMenuRenderer`, bewegt über abstrakte
Eingaben. Dadurch lässt sich das Menü vollständig ohne Spiel durchspielen:

```
.\scripts\build-trainer.ps1 -Test      22 Tests, ohne GTA IV
```

Ein Navigationsfehler fällt so in Millisekunden auf statt nach Spielstart,
Ladebildschirm und Tastendruck.

**Stand T2c.** Vier Gruppen:

| Gruppe | Inhalt |
|---|---|
| Spieler | Godmode, Leben, Panzerung, Fahndungslevel, Geld |
| Waffen | unendlich Munition, alle Waffen, Munition auffüllen |
| Fahrzeuge | zehn Modelle spawnen, reparieren, unkaputtbar |
| Welt | Uhrzeit, Wetter, Verkehrsdichte, fünf Orte anspringen |

Drei Stellen davon sind nicht offensichtlich:

- Gespawnte Modelle gehen über `CStreaming::ScriptRequestModel`, nicht über
  `REQUEST_MODEL` — das ist im SDK auskommentiert. `CREATE_CAR` mit einem nicht
  geladenen Modell erzeugt kein Fahrzeug, sondern **beendet das Spiel**; deshalb
  wird vorher geprüft und im Zweifel nur geloggt.
- Die **Verkehrsdichte wird jeden Frame neu gesetzt**. Das Spiel dreht die
  Multiplikatoren jeden Frame auf `1.0` zurück, ein einmaliges Setzen wäre
  wirkungslos. Die Stufe „normal" ist der Standardwert und fasst nichts an.
- Wetter wechselt mit `FORCE_WEATHER_NOW` statt `FORCE_WEATHER`: letzteres
  blendet über Minuten über und sieht aus dem Menü heraus schlicht kaputt aus.

Godmode wird aus demselben Grund **jeden Frame neu gesetzt**, nicht nur beim Umschalten — das Spiel
nimmt Unverwundbarkeit bei Respawn, Zwischensequenzen und Missionswechseln
zurück. Ein einmal gesetzter Schalter hörte still auf zu wirken, und man hält
dann den Trainer für kaputt statt das Spiel für eigenwillig.

**Alles läuft in `processScriptsEvent`** — Eingabe, Schalter und Zeichnen. Das
war der Ausgang von zwei Fehlern, die erst im Spiel auffielen:

- Spiel-Natives aus `drawingEvent` **beenden das Spiel im Ladebildschirm.** Nur
  `processScriptsEvent` setzt vorher `CTheScripts::m_pCurrentThread`, den
  Kontext den Natives brauchen; und `drawingEvent` läuft laut SDK auch im Menü
  und beim Laden, wo es noch keine Skript-Maschine gibt.
- Zeichnen aus `drawingEvent` landet **im Bildschirm des Handys**, sobald dessen
  Renderziel gebunden ist. Die Skripte des Spiels zeichnen ihr HUD ebenfalls aus
  dem Script-Tick — von dort landen `DRAW_RECT` und `DISPLAY_TEXT` in der
  HUD-Phase, wo sie hingehören.

**Zwei Fallen beim Zeichnen**, beide erst im Spiel sichtbar:

- `DRAW_RECT` nimmt in GTA IV **Mittelpunkt und Größe**, nicht zwei Ecken — die
  Parameternamen im SDK (`x1, y1, x2, y2`) legen anderes nahe. Mit Ecken
  gefüttert landen die Flächen sichtbar daneben.
- `beginFrame` bekommt die Anzahl der Einträge, weil der Hintergrund gezeichnet
  sein muss, **bevor** der Text darauf landet. Später gezeichnete Flächen lägen
  darüber.

Der `VersionAdapter` prüft beim Laden die Spielversion und **bricht ab, wenn
sie nicht unterstützt wird**. Auf einer anderen Version stimmen Native-Hashes
und Speicheradressen nicht, und Schreiben an falschen Adressen fällt nicht
sofort auf, sondern später und an ganz anderer Stelle.

Gearbeitet wird nicht in `DllMain`, sondern in einem eigenen Thread — dort hält
Windows die Loader-Sperre, und wer mehr tut als das Nötigste riskiert einen
Deadlock, der sich als „hängt beim Spielstart" äußert.

Das Logfile heißt `ModlauncherIV-Trainer.log` und wird nach jeder Zeile geleert;
sonst fehlt nach einem Absturz genau die Zeile, die den Grund verraten hätte.

Es wird zuerst neben der DLL angelegt — dort sucht man es. Liegt das Spiel unter
`Program Files` und läuft ohne erhöhte Rechte, scheitert das aber, und dann
weicht es nach `%LOCALAPPDATA%\ModlauncherIV\Trainer.log` aus. **Auf dieser
Installation greift genau der Ausweichpfad.** Ein Trainer ohne Logfile ist bei
einem Problem so stumm wie einer, der gar nicht geladen hat.

Das ASI lädt, erkennt 1.0.7.0 und meldet sich:

```
[14:25:53.944] Modlauncher IV Trainer, Stufe T2c
[14:25:53.945] Version: 1.0.7.0 (1.0.7.0)
[14:25:53.946] Menue bereit. F7 oeffnet, Numblock oder Pfeiltasten bedienen.
```

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
**nicht ins Repository** — er liegt hier unter
`%LOCALAPPDATA%\ModlauncherIV\keys\`. Solange kein Schlüssel eingebaut ist, lehnt
der Launcher jeden Katalog ab; für die Entwicklung gibt es `--allow-unsigned`,
was laut warnt, und `--public-key <Base64>` für einen abweichenden Signierer.

Ein Wechsel des eingebauten Schlüssels macht **jeden bisher signierten Katalog
ungültig**. Das ist gewollt — es ist derselbe Vorgang wie ein Rückruf.

**Wer den Katalog anfasst, muss neu signieren.** Jede Änderung an einer
Rezeptdatei bricht den Index, und der Launcher lädt dann nichts mehr. Das ist
kein Ärgernis, sondern der Sinn der Sache: eine geänderte Rezeptdatei ist von
außen nicht von einer manipulierten zu unterscheiden.

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

**Für den Trainer: gemessen, und es ging schief.**

| | T0 | T1 |
|---|---|---|
| Größe | 147 KB | 193 KB |
| Signatur | selbstsigniert | dieselbe |
| SAC-Zustand | aktiv (`1`) | aktiv (`1`) |
| Ergebnis | **geladen** | **blockiert** |

Gleicher Rechner, gleiches Zertifikat, gleiche Richtlinie — anderes Ergebnis.
T1 scheiterte mit Ereignis 3077 und ASI-Loader-Fehler 4551 (`0x11C7`, der
Win32-Anteil von `0x800711C7`). Die Abhängigkeiten waren sauber, das ASI x86 und
signiert. Es gab technisch nichts zu korrigieren.

Damit ist belegt, was oben als Vermutung steht: **SAC ist keine Regel, die man
erfüllen kann.** Auf diesem Entwicklungsrechner wurde es deshalb abgeschaltet.

**Für Endnutzer bleibt das ungelöst.** Wer Smart App Control aktiv hat, bekommt
den Trainer nicht geladen. Deterministisch hilft dort nur ein echtes
Codesigning-Zertifikat mit aufgebauter Reputation — `MLIV_SIGN_THUMBPRINT`
setzen, dann greift der Release-Pfad in `sign.ps1`.

**Alter Stand, überholt:** Für den Trainer hilft das Single-File-Verfahren
nicht. Eine `.asi` ist definitionsgemäß eine unsignierte DLL, die in `GTAIV.exe`
geladen wird — genau der Vorgang, den SAC unterbindet. Betrifft auch jeden
Endnutzer mit aktivem Smart App Control.

## Meilensteine

- **M0** Erkennung und Diagnosebericht ✔
- **M1** Rezept-Engine, Snapshot, Rollback, Ledger, Dry-Run ✔
- **M2** Beschaffung, Hash-Prüfung, Mirror, Katalogsignatur ✔
- **M3** Downgrade-Rezepte, Versionsgraph, Update-Sperre, Gegenprobe, Rückbau ✔
- **M4** Basis-Stack ✔ · Trainer T0 ✔ · T1 Menügerüst ✔
- **M5** Assistent ✔ · Wegplanung ✔ · Trainer im Katalog ✔ ·
  T2a Spieler ✔ · T2b Waffen ✔ · T2c Fahrzeuge und Welt ✔ ← *hier*
- **M6** Profile, Katalog-Update · Trainer T3: Config und Politur

Offen und bewusst zurückgestellt: **Noclip** braucht einen eigenen Tick-Modus,
und der Trainer hat noch **keine Konfigurationsdatei** — Tastenbelegung steht im
Code.

Der vollständige Projektplan mit Architektur, Risiken und offenen Fragen liegt
als eigenes Dokument vor.

## Grundregeln

- Der Kern fasst nie direkt Dateien an — jede Änderung läuft durch die Pipeline
  aus Pre-Flight, Snapshot, Apply, Verify, Commit, mit automatischem Rollback.
- Ledger und Snapshots liegen unter `%LOCALAPPDATA%\ModlauncherIV\`, nicht im
  Spielverzeichnis.
- Es werden keine Spieldateien und keine fremden Mods mit ausgeliefert. Alles
  wird von der Originalquelle geladen und per SHA-256 geprüft.
