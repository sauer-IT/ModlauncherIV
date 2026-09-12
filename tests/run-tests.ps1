<#
.SYNOPSIS
  Testet die Rezept-Pipeline gegen gefaelschte Spielverzeichnisse.

.DESCRIPTION
  Keine echte Installation wird angefasst. Die Fixtures werden bei jedem Lauf
  neu aufgebaut, damit die Tests unabhaengig voneinander sind.

  Der wichtigste Test ist der letzte: ein Rezept, dessen zweiter Schritt
  fehlschlaegt, muss den ersten Schritt rueckgaengig machen. Genau das ist die
  Eigenschaft, wegen der die ganze Pipeline existiert.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $PSScriptRoot "work"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

# Ein laufendes GTA IV laesst jeden Pre-Flight blockieren - die Tests wuerden
# dann reihenweise fehlschlagen, ohne dass am Code etwas falsch waere. Lieber
# hier einmal klar abbrechen als vier raetselhafte FAILs weiter unten.
if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    Write-Host "GTA IV laeuft. Erst schliessen, sonst blockiert jeder Pre-Flight." -ForegroundColor Red
    exit 2
}

$script:passed = 0
$script:failed = 0

function Assert($condition, $label) {
    if ($condition) {
        Write-Host "  PASS  $label" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  FAIL  $label" -ForegroundColor Red
        $script:failed++
    }
}

function Invoke-Mliv {
    param([string[]] $CliArgs)

    # Unter Windows PowerShell 5.1 macht "Stop" aus jeder stderr-Zeile eines
    # nativen Programms einen abbrechenden NativeCommandError. Unsere Tests
    # pruefen aber gerade die Fehlerfaelle, also hier bewusst nachgeben.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $exe @CliArgs 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

# ---------------------------------------------------------------- Vorbereiten

Write-Host "`n== Bauen ==" -ForegroundColor Cyan
& $dotnet publish (Join-Path $root "src\Launcher.Cli") -c Debug -r win-x64 `
    --self-contained false -p:PublishSingleFile=true -o (Join-Path $root "artifacts\fd") `
    --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }
$exe = Join-Path $root "artifacts\fd\mliv.exe"

Write-Host "`n== Fixtures aufbauen ==" -ForegroundColor Cyan
if (Test-Path $work) { Remove-Item $work -Recurse -Force }

$game = Join-Path $work "game-107"
$cache = Join-Path $work "cache"
$catalog = Join-Path $work "catalog"
New-Item -ItemType Directory -Path $game, $cache, $catalog -Force | Out-Null

# Ein gefaelschtes Spiel. GTAIV.exe muss da sein, sonst wird der Ordner nicht erkannt.
Set-Content -Path (Join-Path $game "GTAIV.exe") -Value "vorgetaeuschte Spieldatei" -NoNewline -Encoding utf8
Set-Content -Path (Join-Path $game "vorhanden.txt") -Value "urspruenglicher Inhalt" -NoNewline -Encoding utf8
New-Item -ItemType Directory -Path (Join-Path $game "belegt") -Force | Out-Null

# Zwei Quelldateien mit echten Pruefsummen.
Set-Content -Path (Join-Path $cache "xliveless.dll") -Value "stellvertretend fuer den GFWL-Stub" -NoNewline -Encoding utf8
Set-Content -Path (Join-Path $cache "loader.dll") -Value "stellvertretend fuer den ASI-Loader" -NoNewline -Encoding utf8

function Get-Sha([string] $p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() }
function Get-Size([string] $p) { (Get-Item $p).Length }

$xliveHash = Get-Sha (Join-Path $cache "xliveless.dll")
$xliveSize = Get-Size (Join-Path $cache "xliveless.dll")
$loaderHash = Get-Sha (Join-Path $cache "loader.dll")
$loaderSize = Get-Size (Join-Path $cache "loader.dll")

# Rezept 1: geht glatt durch.
@"
{
  "id": "test-ok",
  "name": "Testrezept, funktionierend",
  "version": "1.0.0",
  "game": "GtaIV",
  "description": "Kopiert zwei Dateien ins Spielverzeichnis.",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] },
    { "id": "loader", "fileName": "loader.dll", "sha256": "$loaderHash", "sizeBytes": $loaderSize, "urls": [] }
  ],
  "steps": [
    { "type": "copyFile", "source": "xliveless.dll", "target": "xlive.dll" },
    { "type": "copyFile", "source": "loader.dll", "target": "dsound.dll" }
  ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-ok.json") -Encoding utf8

# Rezept 2: Pruefsumme absichtlich falsch -> muss im Pre-Flight blocken.
@"
{
  "id": "test-badhash",
  "name": "Testrezept, falsche Pruefsumme",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "xlive.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-badhash.json") -Encoding utf8

# Rezept 3: Pfad zeigt aus dem Spielverzeichnis heraus -> muss abgelehnt werden.
@"
{
  "id": "test-escape",
  "name": "Testrezept, Pfadausbruch",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "..\\..\\entkommen.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-escape.json") -Encoding utf8

# Rezept 4: Schritt 1 gelingt, Schritt 2 scheitert (Ziel ist ein Verzeichnis).
# Der Pre-Flight kann das nicht sehen -> erzwingt echten Rollback.
@"
{
  "id": "test-rollback",
  "name": "Testrezept, scheitert im zweiten Schritt",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] },
    { "id": "loader", "fileName": "loader.dll", "sha256": "$loaderHash", "sizeBytes": $loaderSize, "urls": [] }
  ],
  "steps": [
    { "type": "copyFile", "source": "xliveless.dll", "target": "vorhanden.txt" },
    { "type": "copyFile", "source": "loader.dll", "target": "belegt" }
  ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-rollback.json") -Encoding utf8

# Zustand aus frueheren Laeufen wegraeumen.
$installs = Join-Path $env:LOCALAPPDATA "ModlauncherIV\installs"
if (Test-Path $installs) {
    Get-ChildItem $installs -Directory | Where-Object { $_.Name -like "game-107-*" } |
        Remove-Item -Recurse -Force
}

# Der Katalog wird standardmaessig nur signiert geladen. Die Rezept-Tests
# arbeiten bewusst unsigniert, die Signatur bekommt einen eigenen Block.
$common = @("--path", $game, "--catalog", $catalog, "--cache", $cache, "--allow-unsigned")

# Quellen fuer die Beschaffungstests.
$fetchCache = Join-Path $work "fetch-cache"
New-Item -ItemType Directory -Path $fetchCache -Force | Out-Null
Copy-Item (Join-Path $cache "xliveless.dll") (Join-Path $fetchCache "xliveless.dll")

@"
{
  "id": "test-fetch-present",
  "name": "Testrezept, Datei liegt schon da",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "xlive.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-fetch-present.json") -Encoding utf8

@"
{
  "id": "test-fetch-missing",
  "name": "Testrezept, Datei fehlt und hat keine Quelle",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "gross", "fileName": "downgrade-paket.zip", "sha256": "1111111111111111111111111111111111111111111111111111111111111111", "sizeBytes": 1234567, "urls": [], "note": "Aus dem Community-Downgrader." }
  ],
  "steps": [ { "type": "extractArchive", "archive": "downgrade-paket.zip", "target": "" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-fetch-missing.json") -Encoding utf8

# Zwei Kanten im Versionsgraphen: 1.2.0.59 -> 1.0.8.0 -> 1.0.7.0.
@"
{
  "id": "test-down-ce-108",
  "name": "Testrezept, CE auf 1.0.8.0",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.2.0.59", "1.2.0.43" ],
  "producesVersion": "1.0.8.0",
  "steps": [ { "type": "ensureDirectory", "target": "downgrade-marker" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-down-ce-108.json") -Encoding utf8

@"
{
  "id": "test-down-108-107",
  "name": "Testrezept, 1.0.8.0 auf 1.0.7.0",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.8.0" ],
  "producesVersion": "1.0.7.0",
  "steps": [ { "type": "ensureDirectory", "target": "downgrade-marker-2" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-down-108-107.json") -Encoding utf8

# Rezepte fuer die Wegplanung. test-j-top haengt an test-j-base, und beide
# gelten nur fuer 1.0.7.0 - also erst nach dem Downgrade.
@"
{
  "id": "test-j-base",
  "name": "Testrezept, Unterbau",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-base" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-base.json") -Encoding utf8

@"
{
  "id": "test-j-top",
  "name": "Testrezept, baut auf dem Unterbau auf",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "requires": [ "test-j-base" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-top" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-top.json") -Encoding utf8

@"
{
  "id": "test-j-streit",
  "name": "Testrezept, vertraegt sich nicht mit dem Unterbau",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "conflictsWith": [ "test-j-base" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-streit" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-streit.json") -Encoding utf8

# Zwei Rezepte, die einander verlangen. Ohne Zyklenpruefung laeuft die
# Aufloesung endlos oder liefert stillschweigend eine falsche Reihenfolge.
@"
{
  "id": "test-j-ring-a",
  "name": "Testrezept, Ring A",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-j-ring-b" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-ring-a" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-ring-a.json") -Encoding utf8

@"
{
  "id": "test-j-ring-b",
  "name": "Testrezept, Ring B",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-j-ring-a" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-ring-b" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-ring-b.json") -Encoding utf8

# Ein gefaelschtes Steam-Layout: Spiel in steamapps/common, Manifest daneben.
$steamRoot = Join-Path $work "steam"
$steamGame = Join-Path $steamRoot "steamapps\common\Grand Theft Auto IV"
New-Item -ItemType Directory -Path $steamGame -Force | Out-Null
Set-Content -Path (Join-Path $steamGame "GTAIV.exe") -Value "vorgetaeuschte Spieldatei" -NoNewline -Encoding utf8
$acf = Join-Path $steamRoot "steamapps\appmanifest_12210.acf"
@'
"AppState"
{
	"appid"		"12210"
	"name"		"Grand Theft Auto IV"
	"AutoUpdateBehavior"		"0"
	"installdir"		"Grand Theft Auto IV"
}
'@ | Set-Content -Path $acf -Encoding utf8

# ------------------------------------------------------------------- Tests

Write-Host "`n== Katalog ==" -ForegroundColor Cyan
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--allow-unsigned")
Assert ($r.ExitCode -eq 0) "catalog: Exitcode 0"
Assert ($r.Output -match "test-ok") "catalog: listet test-ok"
Assert ($r.Output -match "13 Rezept") "catalog: findet alle dreizehn"
Assert ($r.Output -match "NICHT auf eine Signatur") "catalog: warnt vor fehlender Signaturpruefung"

Write-Host "`n== Pruefsummenschutz ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("plan", "test-badhash") + $common)
Assert ($r.ExitCode -eq 3) "badhash: blockiert (Exitcode 3)"
Assert ($r.Output -match "Pr..?fsumme stimmt nicht") "badhash: nennt die Pruefsumme als Grund"

Write-Host "`n== Pfadausbruch ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("plan", "test-escape") + $common)
Assert ($r.ExitCode -eq 3) "escape: blockiert (Exitcode 3)"
Assert ($r.Output -match "heraus") "escape: nennt den Pfadausbruch"
Assert (-not (Test-Path (Join-Path $work "entkommen.dll"))) "escape: nichts ausserhalb geschrieben"

Write-Host "`n== Dry-Run veraendert nichts ==" -ForegroundColor Cyan
$before = (Get-ChildItem $game -Recurse -File).Count
$r = Invoke-Mliv (@("plan", "test-ok") + $common)
$after = (Get-ChildItem $game -Recurse -File).Count
Assert ($r.ExitCode -eq 0) "plan: Exitcode 0"
Assert ($before -eq $after) "plan: Dateizahl unveraendert ($before)"
Assert ($r.Output -match "xlive\.dll") "plan: nennt die Zieldatei"

Write-Host "`n== Ausfuehren ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("apply", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "apply: Exitcode 0"
Assert (Test-Path (Join-Path $game "xlive.dll")) "apply: xlive.dll angelegt"
Assert (Test-Path (Join-Path $game "dsound.dll")) "apply: dsound.dll angelegt"
Assert ((Get-Sha (Join-Path $game "xlive.dll")) -eq $xliveHash) "apply: Inhalt stimmt mit der Quelle ueberein"

Write-Host "`n== Ledger ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("status") + $common)
Assert ($r.ExitCode -eq 0) "status: Exitcode 0"
Assert ($r.Output -match "test-ok") "status: fuehrt test-ok auf"
Assert ($r.Output -match "Snapshot") "status: nennt den Snapshot"

Write-Host "`n== Rollback ==" -ForegroundColor Cyan
$originalContent = Get-Content (Join-Path $game "vorhanden.txt") -Raw
$r = Invoke-Mliv (@("apply", "test-rollback", "--yes") + $common)
$restoredContent = Get-Content (Join-Path $game "vorhanden.txt") -Raw
Assert ($r.ExitCode -eq 5) "rollback: meldet Fehlschlag (Exitcode 5)"
Assert ($r.Output -match "wiederhergestellt") "rollback: meldet die Wiederherstellung"
Assert ($originalContent -eq $restoredContent) "rollback: vorhandene Datei hat ihren alten Inhalt"

$statusAfter = Invoke-Mliv (@("status") + $common)
Assert (-not ($statusAfter.Output -match "test-rollback")) "rollback: kein Ledger-Eintrag fuer den Fehlschlag"

# ------------------------------------------------------------------ verify

Write-Host "`n== Gegenprobe ==" -ForegroundColor Cyan

$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.ExitCode -eq 0) "verify: unveraenderte Installation ist in Ordnung"
Assert ($r.Output -match "Alles unver") "verify: meldet alles unveraendert"

# Eine eingebaute Datei veraendern - genau das tut ein Store-Update.
Set-Content -Path (Join-Path $game "xlive.dll") -Value "vom Store ueberschrieben" -NoNewline -Encoding utf8
$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.ExitCode -eq 3) "verify: veraenderte Datei wird erkannt"
Assert ($r.Output -match "ver.ndert\s+xlive\.dll") "verify: nennt die betroffene Datei"
Assert ($r.Output -match "test-ok") "verify: nennt das zugehoerige Rezept"

# Und eine loeschen.
Remove-Item (Join-Path $game "dsound.dll") -Force
$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.Output -match "fehlt\s+dsound\.dll") "verify: fehlende Datei wird erkannt"

# ------------------------------------------------------------------- route

Write-Host "`n== Versionsgraph ==" -ForegroundColor Cyan
$routeArgs = @("--path", $game, "--catalog", $catalog, "--allow-unsigned", "--assume-version", "1.2.0.59")

$r = Invoke-Mliv (@("route") + $routeArgs)
Assert ($r.ExitCode -eq 0) "route: Exitcode 0"
Assert ($r.Output -match "1\.0\.8\.0") "route: 1.0.8.0 ist erreichbar"
Assert ($r.Output -match "1\.0\.7\.0") "route: 1.0.7.0 ist erreichbar"

$r = Invoke-Mliv (@("route", "1.0.7.0") + $routeArgs)
Assert ($r.ExitCode -eq 0) "route: Weg nach 1.0.7.0 gefunden"
Assert ($r.Output -match "test-down-ce-108") "route: erster Schritt ist die CE-Kante"
Assert ($r.Output -match "test-down-108-107") "route: zweiter Schritt ist die 1.0.8.0-Kante"
Assert ($r.Output -match "2 Rezept") "route: zwei Schritte"

# Regression: FileVersionInfo liefert je nach Binary "1.0.7.0" oder "1, 0, 7, 0".
# Wurden beide Formen unbesehen verglichen, meldete verify einen Rueckpatch, wo
# keiner war - und entwertete damit genau die Warnung, um die es geht.
$r = Invoke-Mliv @("route", "--path", $game, "--catalog", $catalog, "--allow-unsigned",
                   "--assume-version", "1, 2, 0, 59")
Assert ($r.Output -match "1\.0\.7\.0") "normalisierung: Kommaform wird als 1.2.0.59 erkannt"

$r = Invoke-Mliv @("route", "1.0.7.0", "--path", $game, "--catalog", $catalog, "--allow-unsigned",
                   "--assume-version", "1, 0, 7, 0")
Assert ($r.ExitCode -eq 0) "normalisierung: Kommaform des Ziels wird erkannt"
Assert ($r.Output -match "bereits auf") "normalisierung: kein Weg noetig, Version stimmt schon"

$r = Invoke-Mliv (@("route", "9.9.9.9") + $routeArgs)
Assert ($r.ExitCode -eq 1) "route: unerreichbare Version meldet Fehlschlag"
Assert ($r.Output -match "Kein Weg") "route: sagt, dass es keinen Weg gibt"

# ----------------------------------------------------------------- journey

Write-Host "`n== Wegplanung ==" -ForegroundColor Cyan
$jArgs = @("--path", $game, "--catalog", $catalog, "--allow-unsigned")

# Der Kern des Assistenten: test-j-top gilt nur fuer 1.0.7.0. Wer auf 1.2.0.59
# steht, bekommt es trotzdem - denn nach dem Downgrade passt es. Wuerde gegen
# die aktuelle Version geprueft, waere der ganze Weg unmoeglich.
$r = Invoke-Mliv (@("journey", "test-j-top", "--assume-version", "1.2.0.59", "--target", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 0) "journey: Weg ueber das Downgrade ist moeglich"
Assert ($r.Output -match "test-down-ce-108")  "journey: erste Kante ist dabei"
Assert ($r.Output -match "test-down-108-107") "journey: zweite Kante ist dabei"
Assert ($r.Output -match "test-j-base")       "journey: die Abhaengigkeit wurde ergaenzt"
Assert ($r.Output -match "test-j-top")        "journey: das gewuenschte Rezept steht drin"
Assert ($r.Output -match "Offen: 4 von 4")    "journey: vier offene Schritte"

# Reihenfolge: der Versionswechsel muss vor allem anderen stehen, sonst
# ueberschreibt das Downgrade die gerade erst eingebauten Dateien.
$posDown = $r.Output.IndexOf("test-down-ce-108")
$posBase = $r.Output.IndexOf("test-j-base")
$posTop  = $r.Output.IndexOf("test-j-top")
Assert ($posDown -lt $posBase) "journey: Downgrade steht vor den Rezepten"
Assert ($posBase -lt $posTop)  "journey: Abhaengigkeit steht vor dem, was sie braucht"

# Ohne Downgrade passt test-j-top nicht - das muss auffallen, nicht durchrutschen.
$r = Invoke-Mliv (@("journey", "test-j-top", "--assume-version", "1.2.0.59") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: ohne Downgrade blockiert"
Assert ($r.Output -match "passt nicht zu Version") "journey: nennt die unpassende Version"

$r = Invoke-Mliv (@("journey", "test-j-ring-a") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: Ringabhaengigkeit blockiert"
Assert ($r.Output -match "verlangen einander") "journey: nennt den Ring"
Assert ($r.Output -match "test-j-ring-a -> test-j-ring-b") "journey: zeigt den Ring als Kette"

$r = Invoke-Mliv (@("journey", "test-j-base,test-j-streit", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: Konflikt blockiert"
Assert ($r.Output -match "vertraegt sich nicht|verträgt sich nicht") "journey: nennt den Konflikt"

$r = Invoke-Mliv (@("journey", "gibt-es-nicht") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: unbekanntes Rezept blockiert"
Assert ($r.Output -match "steht nicht im Katalog") "journey: sagt, dass es das Rezept nicht gibt"

# Ohne lesbare Version gibt es keinen Ausgangspunkt - und damit keinen Weg.
$r = Invoke-Mliv (@("journey", "test-j-base", "--target", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: unbekannte Ausgangsversion blockiert"
Assert ($r.Output -match "nicht bestimmen") "journey: sagt, dass die Version fehlt"

# Die fehlende Version ist die Ursache. Sie danach noch einmal pro Rezept als
# "passt nicht zu (keine Versionsinformation)" zu melden, vergraebt sie.
Assert ($r.Output -notmatch "passt nicht zu Version") "journey: keine Folgemeldung zur fehlenden Version"

# Auch ohne Zielversion darf eine unlesbare Version nicht stillschweigend
# durchgehen - sonst wuerde ungeprueft installiert.
$r = Invoke-Mliv (@("journey", "test-j-base") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: unbekannte Version blockiert auch ohne Ziel"

$r = Invoke-Mliv (@("journey", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 0) "journey: ohne Wuensche ist nichts zu tun"
Assert ($r.Output -match "nichts zu tun") "journey: sagt das auch"

# ------------------------------------------------------------------- guard

Write-Host "`n== Update-Sperre ==" -ForegroundColor Cyan

$r = Invoke-Mliv @("guard", "--path", $steamGame)
Assert ($r.ExitCode -eq 3) "guard: offene Steam-Installation wird beanstandet"
Assert ($r.Output -match "Steam") "guard: erkennt Steam am Manifest oberhalb des Ordners"
Assert ($r.Output -match "jederzeit aktualisieren") "guard: nennt den offenen Zustand"

$r = Invoke-Mliv @("guard", "--path", $steamGame, "--apply")
Assert ($r.ExitCode -eq 0) "guard --apply: Exitcode 0"
Assert (Test-Path "$acf.mliv-backup") "guard --apply: Sicherung des Manifests angelegt"
Assert ((Get-Content $acf -Raw) -match '"AutoUpdateBehavior"\s*"1"') "guard --apply: Schalter gesetzt"

$r = Invoke-Mliv @("guard", "--path", $steamGame)
Assert ($r.ExitCode -eq 0) "guard: gesperrte Installation ist in Ordnung"
Assert ($r.Output -match "nur beim Starten") "guard: meldet die Sperre"

$r = Invoke-Mliv (@("guard") + $common)
Assert ($r.Output -match "Herkunft der Installation unbekannt") "guard: unbekannte Herkunft wird als solche gemeldet"
Assert (-not ($r.Output -match "AutoUpdateBehavior")) "guard: kein Steam-Schalter bei unbekannter Herkunft"

# ------------------------------------------------------------- Beschaffung

Write-Host "`n== Beschaffung ==" -ForegroundColor Cyan
$fetchArgs = @("--catalog", $catalog, "--cache", $fetchCache, "--allow-unsigned")

$r = Invoke-Mliv (@("fetch", "test-fetch-present") + $fetchArgs)
Assert ($r.ExitCode -eq 0) "fetch: vorhandene, korrekte Datei wird akzeptiert"
Assert ($r.Output -match "lag bereits vor") "fetch: meldet sie als bereits vorhanden"

# Datei im Arbeitsverzeichnis verfaelschen -> muss verworfen werden.
Set-Content -Path (Join-Path $fetchCache "xliveless.dll") -Value "manipuliert" -NoNewline -Encoding utf8
$r = Invoke-Mliv (@("fetch", "test-fetch-present") + $fetchArgs)
Assert ($r.ExitCode -eq 5) "fetch: falsche Pruefsumme im Cache wird nicht akzeptiert"
Assert ($r.Output -match "falsche Pr..?fsumme") "fetch: nennt die Pruefsumme als Grund"
Assert (-not (Test-Path (Join-Path $fetchCache "xliveless.dll"))) "fetch: verfaelschte Datei wurde entfernt"

$r = Invoke-Mliv (@("fetch", "test-fetch-missing") + $fetchArgs)
Assert ($r.ExitCode -eq 5) "fetch: fehlende Datei ohne Quelle meldet Fehlschlag"
Assert ($r.Output -match "VON HAND ABZULEGEN") "fetch: gibt eine Anleitung zum Selbstablegen"
Assert ($r.Output -match "1111111111") "fetch: nennt die erwartete Pruefsumme"
Assert ($r.Output -match "Community-Downgrader") "fetch: gibt den Hinweis aus dem Rezept weiter"

# ----------------------------------------------------------- Lieferumfang

Write-Host "`n== Lieferumfang ==" -ForegroundColor Cyan

# Der Launcher bringt eine Datei selbst mit - den eigenen Trainer. Sie liegt
# neben dem Programm statt im Netz. Geprueft wird sie trotzdem: der Ordner ist
# beschreibbar fuer jeden, der dort Rechte hat, also ist Mitliefern kein
# Vertrauensbonus.
$bundledDir = Join-Path $root "artifacts\fd\bundled"
New-Item -ItemType Directory -Path $bundledDir -Force | Out-Null
$bundledFile = Join-Path $bundledDir "mitgeliefert.dll"
Set-Content -Path $bundledFile -Value "stellvertretend fuer den Trainer" -NoNewline -Encoding utf8

$bundledHash = Get-Sha $bundledFile
$bundledSize = Get-Size $bundledFile

@"
{
  "id": "test-bundled",
  "name": "Testrezept, mitgelieferte Datei",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "mit", "fileName": "mitgeliefert.dll", "sha256": "$bundledHash", "sizeBytes": $bundledSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "mitgeliefert.dll", "target": "mitgeliefert.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-bundled.json") -Encoding utf8

$bundledCache = Join-Path $work "bundled-cache"
New-Item -ItemType Directory -Path $bundledCache -Force | Out-Null
$bundledArgs = @("--catalog", $catalog, "--cache", $bundledCache, "--allow-unsigned")

$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 0) "lieferumfang: Datei wird uebernommen"
Assert ($r.Output -match "mitgeliefert\s") "lieferumfang: wird als mitgeliefert ausgewiesen"
Assert (Test-Path (Join-Path $bundledCache "mitgeliefert.dll")) "lieferumfang: liegt im Arbeitsverzeichnis"
Assert ((Get-Sha (Join-Path $bundledCache "mitgeliefert.dll")) -eq $bundledHash) "lieferumfang: Inhalt stimmt"

# Der eigentliche Zweck der Pruefung: jemand tauscht die mitgelieferte Datei
# aus. Ohne den Pruefsummenvergleich landete beliebiger Code im Spiel.
Remove-Item (Join-Path $bundledCache "mitgeliefert.dll") -Force
Set-Content -Path $bundledFile -Value "ausgetauscht" -NoNewline -Encoding utf8

$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 5) "lieferumfang: ausgetauschte Datei wird abgelehnt"
Assert ($r.Output -match "falscher Pr..?fsumme|falsche Pr..?fsumme") "lieferumfang: nennt die Pruefsumme als Grund"
Assert (-not (Test-Path (Join-Path $bundledCache "mitgeliefert.dll"))) "lieferumfang: nichts ins Arbeitsverzeichnis gelangt"

Remove-Item $bundledFile -Force
$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 5) "lieferumfang: fehlende Datei meldet Fehlschlag"

# ------------------------------------------------------- Download und Mirror

Write-Host "`n== Download und Mirror ==" -ForegroundColor Cyan

$port = 18734
$srvDir = Join-Path $work "server"
New-Item -ItemType Directory -Path $srvDir -Force | Out-Null

# Beide Dateien exakt gleich lang: sonst greift schon die Groessenpruefung und
# die Pruefsummenablehnung waehrend des Downloads bliebe ungetestet.
Set-Content (Join-Path $srvDir "good.bin") -Value "OK-Nutzdaten-fuer-den-Test" -NoNewline -Encoding ascii
Set-Content (Join-Path $srvDir "bad.bin")  -Value "XX-Nutzdaten-fuer-den-Test" -NoNewline -Encoding ascii

$goodHash = Get-Sha (Join-Path $srvDir "good.bin")
$goodSize = Get-Size (Join-Path $srvDir "good.bin")

# Drei Quellen: 404, dann falscher Inhalt, dann die richtige. Nur wenn der
# Acquirer beide Fehlschlaege ueberlebt, kommt er zur dritten.
@"
{
  "id": "test-mirror",
  "name": "Testrezept, Mirror-Kette",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "nutzdaten", "fileName": "nutzdaten.bin", "sha256": "$goodHash", "sizeBytes": $goodSize,
      "urls": [
        "http://localhost:$port/gibtsnicht.bin",
        "http://localhost:$port/bad.bin",
        "http://localhost:$port/good.bin"
      ] }
  ],
  "steps": [ { "type": "copyFile", "source": "nutzdaten.bin", "target": "nutzdaten.bin" } ]
}
"@ | Set-Content -Path (Join-Path $work "test-mirror.json") -Encoding utf8

# Haengt an test-ok - fuer die Abhaengigkeitspruefung beim Rueckbau.
@"
{
  "id": "test-needs-ok",
  "name": "Testrezept, braucht test-ok",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-ok" ],
  "steps": [ { "type": "ensureDirectory", "target": "haengt-dran" } ]
}
"@ | Set-Content -Path (Join-Path $work "test-needs-ok.json") -Encoding utf8

$server = Start-Job -ScriptBlock {
    param($p, $dir)
    $listener = [System.Net.HttpListener]::new()
    $listener.Prefixes.Add("http://localhost:$p/")
    $listener.Start()
    try {
        for ($i = 0; $i -lt 12; $i++) {
            $ctx = $listener.GetContext()
            $name = $ctx.Request.Url.AbsolutePath.TrimStart('/')
            $file = Join-Path $dir $name
            if (Test-Path $file) {
                $bytes = [System.IO.File]::ReadAllBytes($file)
                $ctx.Response.ContentLength64 = $bytes.Length
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            } else {
                $ctx.Response.StatusCode = 404
            }
            $ctx.Response.Close()
        }
    } finally { $listener.Stop() }
} -ArgumentList $port, $srvDir

# Auf Bereitschaft warten, statt blind loszulaufen.
$ready = $false
foreach ($attempt in 1..20) {
    try {
        $probe = [System.Net.WebRequest]::Create("http://localhost:$port/good.bin")
        $probe.Timeout = 500
        $probe.GetResponse().Close()
        $ready = $true
        break
    } catch {
        if ($_.Exception.Response) { $ready = $true; break }
        Start-Sleep -Milliseconds 150
    }
}

if (-not $ready) {
    Write-Host "  SKIP  lokaler HTTP-Server nicht startbar (Port $port belegt oder ACL) - Downloadtests uebersprungen" -ForegroundColor Yellow
} else {
    $dlCache = Join-Path $work "dl-cache"
    New-Item -ItemType Directory -Path $dlCache -Force | Out-Null

    Copy-Item (Join-Path $work "test-mirror.json") (Join-Path $catalog "test-mirror.json")

    $r = Invoke-Mliv @("fetch", "test-mirror", "--catalog", $catalog, "--cache", $dlCache, "--allow-unsigned")
    Assert ($r.ExitCode -eq 0) "download: Datei ueber die Mirror-Kette beschafft"
    Assert (Test-Path (Join-Path $dlCache "nutzdaten.bin")) "download: Datei liegt im Arbeitsverzeichnis"
    Assert ((Get-Sha (Join-Path $dlCache "nutzdaten.bin")) -eq $goodHash) "download: Inhalt ist der erwartete"
    Assert ($r.Output -match "gibtsnicht\.bin") "download: nennt den fehlgeschlagenen ersten Mirror"
    Assert ($r.Output -match "bad\.bin") "download: nennt den zweiten Mirror mit falschem Inhalt"
    Assert (-not (Test-Path (Join-Path $dlCache "nutzdaten.bin.part"))) "download: keine .part-Reste"

    # Zweiter Lauf: nichts mehr zu tun, kein erneuter Netzzugriff noetig.
    $r = Invoke-Mliv @("fetch", "test-mirror", "--catalog", $catalog, "--cache", $dlCache, "--allow-unsigned")
    Assert ($r.ExitCode -eq 0) "download: zweiter Lauf ist erfolgreich"
    Assert ($r.Output -match "lag bereits vor") "download: zweiter Lauf laedt nicht erneut"

    Remove-Item (Join-Path $catalog "test-mirror.json") -Force
}

Stop-Job $server -ErrorAction SilentlyContinue | Out-Null
Remove-Job $server -Force -ErrorAction SilentlyContinue | Out-Null

# ------------------------------------------------------------ Katalogsignatur

Write-Host "`n== Katalogsignatur ==" -ForegroundColor Cyan

$r = Invoke-Mliv @("catalog", "--catalog", $catalog)
Assert ($r.ExitCode -ne 0) "signatur: unsignierter Katalog wird ohne Flag abgelehnt"
Assert ($r.Output -match "nicht vertrauensw") "signatur: nennt fehlendes Vertrauen als Grund"
Assert (-not ($r.Output -match "test-ok\s")) "signatur: laedt kein einziges Rezept"

$keyFile = Join-Path $work "catalog-signing-key.pem"
$r = Invoke-Mliv @("catalog-key", "--key", $keyFile)
Assert ($r.ExitCode -eq 0) "catalog-key: Exitcode 0"
Assert (Test-Path $keyFile) "catalog-key: privaten Schluessel geschrieben"
Assert ($r.Output -match "EmbeddedPublicKey") "catalog-key: nennt den oeffentlichen Teil zum Eintragen"

$publicKey = ([regex]::Match($r.Output, 'EmbeddedPublicKey = "([^"]+)"')).Groups[1].Value
Assert ($publicKey.Length -gt 40) "catalog-key: oeffentlicher Schluessel ist brauchbar"

$r = Invoke-Mliv @("catalog-key", "--key", $keyFile)
Assert ($r.ExitCode -ne 0) "catalog-key: bestehender Schluessel wird nicht ueberschrieben"

$r = Invoke-Mliv @("catalog-sign", "--catalog", $catalog, "--key", $keyFile)
Assert ($r.ExitCode -eq 0) "catalog-sign: Exitcode 0"
Assert (Test-Path (Join-Path $catalog "index.json")) "catalog-sign: Index angelegt"
Assert (Test-Path (Join-Path $catalog "index.json.sig")) "catalog-sign: Signatur angelegt"

$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -eq 0) "signatur: signierter Katalog wird akzeptiert"
Assert ($r.Output -match "14 Rezept") "signatur: laedt alle Rezepte aus dem Index"
Assert (-not ($r.Output -match "NICHT auf eine Signatur")) "signatur: keine Unsigniert-Warnung"

# Eine Rezeptdatei nach dem Signieren aendern. Der Index ist signiert, also muss
# die Abweichung auffallen - und nur diese eine Datei darf wegfallen.
$tampered = Join-Path $catalog "test-ok.json"
(Get-Content $tampered -Raw).Replace('"target": "xlive.dll"', '"target": "boese.dll"') |
    Set-Content $tampered -Encoding utf8 -NoNewline
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -ne 0) "manipulation: veraenderte Rezeptdatei faellt auf"
Assert ($r.Output -match "weicht vom signierten Index ab") "manipulation: nennt den Indexvergleich"
Assert (-not ($r.Output -match "boese\.dll")) "manipulation: das manipulierte Rezept wird nicht geladen"
Assert ($r.Output -match "test-rollback") "manipulation: unveraenderte Rezepte bleiben nutzbar"

# Die Signatur selbst faelschen.
Set-Content (Join-Path $catalog "index.json.sig") -Value ([Convert]::ToBase64String((1..64))) -Encoding utf8 -NoNewline
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -ne 0) "manipulation: gefaelschte Signatur wird abgelehnt"
Assert ($r.Output -match "ung..?ltig|nicht vertrauensw") "manipulation: nennt die Signatur als Grund"

# -------------------------------------------------------------------- remove

Write-Host "`n== Rueckbau ==" -ForegroundColor Cyan

# Ein Rezept, das test-ok voraussetzt - damit die Abhaengigkeitspruefung greift.
Copy-Item (Join-Path $work "test-needs-ok.json") (Join-Path $catalog "test-needs-ok.json")
$r = Invoke-Mliv (@("apply", "test-needs-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "rueckbau: abhaengiges Rezept installiert"

# test-ok entfernen, waehrend test-needs-ok daran haengt -> muss abgelehnt werden.
$r = Invoke-Mliv (@("remove", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 5) "rueckbau: gebundenes Rezept wird nicht entfernt"
Assert ($r.Output -match "setzt test-ok voraus") "rueckbau: nennt das abhaengige Rezept"
Assert (Test-Path (Join-Path $game "xlive.dll")) "rueckbau: nichts wurde angefasst"

# Ohne Argument und ohne --all: Hinweis statt Raten.
$r = Invoke-Mliv (@("remove") + $common)
Assert ($r.ExitCode -eq 2) "rueckbau: ohne Angabe wird nicht geraten"
Assert ($r.Output -match "remove --all") "rueckbau: nennt den Weg fuer alles"

# Alles zurueck, neueste zuerst - damit loest sich die Abhaengigkeit von selbst.
$r = Invoke-Mliv (@("remove", "--all", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "rueckbau: --all laeuft durch"
Assert (-not (Test-Path (Join-Path $game "xlive.dll"))) "rueckbau: neu angelegte Datei ist wieder weg"
Assert (-not (Test-Path (Join-Path $game "dsound.dll"))) "rueckbau: zweite neue Datei ist wieder weg"

# Die Datei, die es vorher schon gab, muss ihren urspruenglichen Inhalt haben -
# Loeschen allein wuerde sie nicht zurueckbringen.
Assert ((Get-Content (Join-Path $game "vorhanden.txt") -Raw) -eq $originalContent) `
    "rueckbau: vorbestehende Datei hat wieder ihren alten Inhalt"

$r = Invoke-Mliv (@("status") + $common)
Assert (-not ($r.Output -match "test-ok")) "rueckbau: Ledger ist leer"

$r = Invoke-Mliv (@("remove", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 5) "rueckbau: was nicht installiert ist, laesst sich nicht entfernen"

# ------------------------------------------------------------------ Ergebnis

Write-Host "`n$('=' * 50)"
Write-Host " $script:passed bestanden, $script:failed fehlgeschlagen" -ForegroundColor $(if ($script:failed) { "Red" } else { "Green" })
Write-Host "$('=' * 50)`n"

exit $(if ($script:failed) { 1 } else { 0 })
