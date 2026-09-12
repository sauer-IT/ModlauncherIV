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

$common = @("--path", $game, "--catalog", $catalog, "--cache", $cache)

# ------------------------------------------------------------------- Tests

Write-Host "`n== Katalog ==" -ForegroundColor Cyan
$r = Invoke-Mliv @("catalog", "--catalog", $catalog)
Assert ($r.ExitCode -eq 0) "catalog: Exitcode 0"
Assert ($r.Output -match "test-ok") "catalog: listet test-ok"
Assert ($r.Output -match "4 Rezept") "catalog: findet alle vier"

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

# ------------------------------------------------------------------ Ergebnis

Write-Host "`n$('=' * 50)"
Write-Host " $script:passed bestanden, $script:failed fehlgeschlagen" -ForegroundColor $(if ($script:failed) { "Red" } else { "Green" })
Write-Host "$('=' * 50)`n"

exit $(if ($script:failed) { 1 } else { 0 })
