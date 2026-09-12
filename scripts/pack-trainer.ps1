<#
.SYNOPSIS
  Baut den Trainer und macht ihn im Katalog installierbar.

.DESCRIPTION
  Der Trainer ist die einzige Datei im Katalog, die wir selbst herstellen. Sein
  Rezept laesst sich deshalb nicht von Hand pflegen: Pruefsumme und Groesse
  aendern sich bei jedem Build, und ein Rezept mit einer Pruefsumme von gestern
  lehnt genau die Datei ab, die es installieren soll.

  Also wird das Rezept erzeugt, nicht geschrieben:

    1. Trainer bauen
    2. ASI in den Lieferumfang legen (src\Launcher.App\bundled)
    3. catalog\mliv-trainer.json mit gemessener Pruefsumme schreiben
    4. Katalog neu signieren - die Aenderung macht den alten Index ungueltig

  Ohne Schritt 4 laedt der Launcher anschliessend gar nichts mehr, und zwar zu
  Recht: eine Rezeptdatei, die nicht zum signierten Index passt, ist aus seiner
  Sicht nicht von einer manipulierten zu unterscheiden.

  Der Trainer hat bewusst keine URL. Ihn irgendwo abzulegen, damit der eigene
  Launcher ihn wieder herunterlaedt, waere ein Umweg mit einer zusaetzlichen
  Fehlerquelle - er liegt neben dem Programm und wird von dort uebernommen.

  appliesToVersions nennt nur 1.0.7.0, obwohl der Launcher auch 1.0.8.0 und
  1.0.4.0 kennt. Der Grund steht in GameVersion.cpp: der Trainer laeuft nur auf
  1.0.7.0 und weigert sich auf allem anderen zu laden. Stuende hier mehr, wuerde
  der Assistent ihn auf 1.0.8.0 bereitwillig einbauen, und der Nutzer haette
  eine Datei im plugins-Ordner, die wortlos nichts tut.

.PARAMETER Key
  Privater Katalogschluessel. Ohne ihn wird gebaut, aber nicht signiert.

.EXAMPLE
  .\scripts\pack-trainer.ps1
  .\scripts\pack-trainer.ps1 -Key "$env:LOCALAPPDATA\ModlauncherIV\keys\catalog-signing.pem"
#>
[CmdletBinding()]
param(
    [string] $Key = (Join-Path $env:LOCALAPPDATA "ModlauncherIV\keys\catalog-signing.pem")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# ------------------------------------------------------------------- 1. Bauen

Write-Host "Baue den Trainer ..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "build-trainer.ps1")
if ($LASTEXITCODE -ne 0) { throw "Der Trainer-Build ist fehlgeschlagen." }

$asi = Join-Path $root "artifacts\trainer\ModlauncherIV-Trainer.asi"
if (-not (Test-Path $asi)) { throw "Gebaute ASI nicht gefunden: $asi" }

# --------------------------------------------------------- 2. In den Lieferumfang

$bundled = Join-Path $root "src\Launcher.App\bundled"
New-Item -ItemType Directory -Path $bundled -Force | Out-Null
Copy-Item $asi (Join-Path $bundled "ModlauncherIV-Trainer.asi") -Force

$hash = (Get-FileHash $asi -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $asi).Length

Write-Host "  SHA-256  $hash"
Write-Host "  Groesse  $size Bytes"

# Die Rezeptfassung traegt die Pruefsumme im Namen.
#
# Der Grund steht sonst rot auf der Startseite: der Planer vergleicht
# Rezeptfassungen, nicht Dateiinhalte. Bliebe die Fassung bei jedem Build
# dieselbe, haette ein neu gebauter Trainer dieselbe Nummer wie der
# installierte - der Planer haelt ihn fuer erledigt, und der Nutzer sieht
# zwar "Datei veraendert", bekommt aber nichts angeboten, was es richtet.
#
# Mit der Pruefsumme im Namen erzeugt jeder Build eine neue Fassung, und
# eine Aktualisierung wird genau dann angeboten, wenn sich wirklich etwas
# geaendert hat.
$feature = "0.4.0"
$version = "$feature+$($hash.Substring(0, 8))"

Write-Host "  Fassung  $version"

# ------------------------------------------------------------------ 3. Rezept

$recipe = @"
{
  "id": "mliv-trainer",
  "name": "Modlauncher IV Trainer",
  "version": "$version",
  "game": "GtaIV",
  "description": "Das Trainer-Menue dieses Projekts. Im Spiel oeffnet F7; bedient wird mit dem Numblock oder den Pfeiltasten. Spieler, Waffen, Fahrzeuge und Welt. Tastenbelegung und Menuelage stehen in ModlauncherIV-Trainer.ini, die beim ersten Start angelegt wird.",

  "appliesToVersions": [ "1.0.7.0" ],

  "requires": [ "ultimate-asi-loader" ],

  "sources": [
    {
      "id": "asi",
      "fileName": "ModlauncherIV-Trainer.asi",
      "sha256": "$hash",
      "sizeBytes": $size,
      "urls": [],
      "note": "Wird vom Launcher mitgeliefert und nicht heruntergeladen. Diese Datei erzeugt scripts\\pack-trainer.ps1; die Pruefsumme wird beim Bauen gemessen."
    }
  ],

  "steps": [
    { "type": "ensureDirectory", "target": "plugins" },
    { "type": "copyFile", "source": "ModlauncherIV-Trainer.asi", "target": "plugins\\ModlauncherIV-Trainer.asi" }
  ]
}
"@

$recipePath = Join-Path $root "catalog\mliv-trainer.json"
Set-Content -Path $recipePath -Value $recipe -Encoding utf8
Write-Host "Rezept geschrieben: $recipePath" -ForegroundColor Green

# --------------------------------------------------------------- 4. Signieren

if (-not (Test-Path $Key)) {
    Write-Host ""
    Write-Host "Kein Schluessel unter $Key - der Katalog bleibt unsigniert." -ForegroundColor Yellow
    Write-Host "Der Launcher wird dann KEIN Rezept laden. Zum Signieren:" -ForegroundColor Yellow
    Write-Host "  .\scripts\pack-trainer.ps1 -Key <pfad\zum\schluessel.pem>" -ForegroundColor Yellow
    exit 0
}

$mliv = Join-Path $root "artifacts\fd\mliv.exe"
if (-not (Test-Path $mliv)) {
    Write-Host "Baue die CLI, um den Katalog zu signieren ..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $root "src\Launcher.Cli") -c Release -o (Join-Path $root "artifacts\fd") -v q
}

# Native stderr bringt Windows PowerShell unter "Stop" zum Abbruch, auch wenn
# der Aufruf gelingt - hier bewusst nachgeben.
$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $mliv catalog-sign --catalog (Join-Path $root "catalog") --key $Key
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($code -ne 0) { throw "Das Signieren ist fehlgeschlagen (Exitcode $code)." }

Write-Host ""
Write-Host "Fertig. Der Trainer ist jetzt ueber den Assistenten installierbar." -ForegroundColor Green
