<#
.SYNOPSIS
  Startet GTA IV direkt, am Rockstar Games Launcher vorbei.

.DESCRIPTION
  Nach dem Downgrade darf das Spiel nicht ueber den Launcher gestartet werden:
  der bemerkt die veraenderte Installation und setzt sie im Zweifel zurueck.
  Dieses Skript startet GTAIV.exe direkt und holt das Fenster nach vorn.

  Der Trainer startet nicht separat - er liegt als .asi im plugins-Ordner und
  wird vom ASI-Loader beim Spielstart mitgeladen. Im Spiel oeffnet F7 das Menue.

.PARAMETER Log
  Zeigt das Trainer-Logfile mit, waehrend das Spiel laeuft.

.EXAMPLE
  .\scripts\play.ps1
  .\scripts\play.ps1 -Log
#>
[CmdletBinding()]
param(
    [switch] $Log,
    [string] $GamePath = "C:\Program Files\Rockstar Games\Grand Theft Auto IV"
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $GamePath "GTAIV.exe"
if (-not (Test-Path $exe)) { throw "GTAIV.exe nicht gefunden unter $GamePath" }

if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    Write-Host "Das Spiel laeuft bereits." -ForegroundColor Yellow
    exit 0
}

Write-Host "Starte GTA IV ..." -ForegroundColor Cyan
$game = Start-Process -FilePath $exe -WorkingDirectory $GamePath -PassThru

# Auf das Fenster warten, statt blind nach vorn zu holen - vorher gibt es keins.
foreach ($attempt in 1..30) {
    Start-Sleep -Seconds 2
    $proc = Get-Process -Id $game.Id -ErrorAction SilentlyContinue
    if (-not $proc) { throw "Das Spiel hat sich beendet. Trainer-Log pruefen." }
    if ($proc.MainWindowHandle -ne 0) { break }
}

$sig = '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);'
$fg = Add-Type -MemberDefinition $sig -Name Foreground -Namespace MlivPlay -PassThru
$proc = Get-Process -Id $game.Id -ErrorAction SilentlyContinue
if ($proc -and $proc.MainWindowHandle -ne 0) { $fg::SetForegroundWindow($proc.MainWindowHandle) | Out-Null }

Write-Host "Laeuft. F7 oeffnet das Trainer-Menue." -ForegroundColor Green

if (-not $Log) { exit 0 }

# Das Log liegt dort, wo der Trainer es anlegen durfte - neben der DLL, oder
# unter LOCALAPPDATA, wenn das Spielverzeichnis nicht beschreibbar war.
$candidates = @(
    (Join-Path $GamePath "plugins\ModlauncherIV-Trainer.log"),
    (Join-Path $env:LOCALAPPDATA "ModlauncherIV\Trainer.log")
)

$logFile = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $logFile) {
    Write-Host "Kein Trainer-Logfile gefunden - laedt das ASI ueberhaupt?" -ForegroundColor Yellow
    exit 0
}

Write-Host "`nLog: $logFile  (Strg+C beendet das Mitlesen, nicht das Spiel)`n" -ForegroundColor Cyan
Get-Content -LiteralPath $logFile -Wait -Tail 50
