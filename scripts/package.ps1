<#
.SYNOPSIS
  Baut die auslieferbare Fassung: eine einzige EXE zum Anklicken.

.DESCRIPTION
  Selbstenthaltend, das heisst mit kompletter .NET-Laufzeit im Inneren. Das
  kostet rund 150 MB, spart dem Nutzer aber genau das, woran solche Werkzeuge
  sonst scheitern: eine Fehlermeldung ueber eine fehlende Runtime statt eines
  Programms. .NET 10 ist neu genug, dass fast niemand es installiert hat.

  Katalog und mitgelieferter Trainer wandern mit in die EXE
  (IncludeAllContentForSelfExtract). Beim Start entpackt .NET sie in einen
  Ordner unter TEMP, und AppContext.BaseDirectory zeigt dorthin - genau der
  Pfad, unter dem AppPaths Katalog und Lieferumfang sucht. Damit bleibt es
  eine Datei, ohne dass der Code etwas von Verpackung wissen muss.

  Reihenfolge ist wichtig: erst der Trainer, dann das Signieren des Katalogs,
  dann die EXE. Wer die EXE zuerst baut, packt den alten Katalog ein.

.PARAMETER Key
  Privater Katalogschluessel fuer die Signatur.

.PARAMETER SkipTrainer
  Ueberspringt den Trainer-Build. Nur sinnvoll, wenn sich am Trainer nichts
  geaendert hat - sonst passt die Pruefsumme im Rezept nicht zur Datei.

.EXAMPLE
  .\scripts\package.ps1
#>
[CmdletBinding()]
param(
    [string] $Key = (Join-Path $env:LOCALAPPDATA "ModlauncherIV\keys\catalog-signing.pem"),
    [switch] $SkipTrainer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\release"

# --------------------------------------------------- 1. Trainer und Katalog

if (-not $SkipTrainer) {
    Write-Host "== Trainer bauen und Katalog signieren ==" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "pack-trainer.ps1") -Key $Key
    if ($LASTEXITCODE -ne 0) { throw "pack-trainer ist fehlgeschlagen." }
}

$index = Join-Path $root "catalog\index.json.sig"
if (-not (Test-Path $index)) {
    throw "Der Katalog ist nicht signiert. Ohne Signatur laedt der Launcher kein Rezept."
}

# ----------------------------------------------------------------- 2. Bauen

Write-Host "`n== Die EXE bauen ==" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & dotnet publish (Join-Path $root "src\Launcher.App") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $out -v q --nologo
    $code = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previous }

if ($code -ne 0) { throw "Der Build ist fehlgeschlagen (Exitcode $code)." }

$exe = Join-Path $out "ModlauncherIV.exe"
if (-not (Test-Path $exe)) { throw "ModlauncherIV.exe wurde nicht erzeugt." }

# Alles, was nicht die eine Datei ist, waere ein Widerspruch zum Zweck der
# Uebung. Bleibt etwas liegen, soll es auffallen statt mitgeliefert zu werden.
$extra = Get-ChildItem $out -File | Where-Object { $_.Name -ne "ModlauncherIV.exe" }
if ($extra) {
    Write-Host "Neben der EXE liegt noch:" -ForegroundColor Yellow
    $extra | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Yellow }
}

# -------------------------------------------------------------- 3. Signieren

& (Join-Path $PSScriptRoot "sign.ps1") -Path $exe

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "Fertig: $exe ($size MB)" -ForegroundColor Green
Write-Host "Eine Datei. Doppelklick genuegt - .NET muss nicht installiert sein." -ForegroundColor Green
Write-Host ""
Write-Host "Beim Start fragt Windows nach Administratorrechten. Das ist so gewollt:" -ForegroundColor DarkGray
Write-Host "GTA IV liegt unter Program Files, und dorthin schreibt niemand ohne." -ForegroundColor DarkGray
