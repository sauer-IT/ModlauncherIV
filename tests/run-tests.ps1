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

# ------------------------------------------------------------------- Tests

Write-Host "`n== Katalog ==" -ForegroundColor Cyan
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--allow-unsigned")
Assert ($r.ExitCode -eq 0) "catalog: Exitcode 0"
Assert ($r.Output -match "test-ok") "catalog: listet test-ok"
Assert ($r.Output -match "6 Rezept") "catalog: findet alle sechs"
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
Assert ($r.Output -match "6 Rezept") "signatur: laedt alle Rezepte aus dem Index"
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

# ------------------------------------------------------------------ Ergebnis

Write-Host "`n$('=' * 50)"
Write-Host " $script:passed bestanden, $script:failed fehlgeschlagen" -ForegroundColor $(if ($script:failed) { "Red" } else { "Green" })
Write-Host "$('=' * 50)`n"

exit $(if ($script:failed) { 1 } else { 0 })
