<#
.SYNOPSIS
  Starts GTA IV directly, bypassing the Rockstar Games Launcher.

.DESCRIPTION
  After the downgrade the game must not be started through the launcher: it
  notices the changed installation and, in case of doubt, resets it. This script
  starts GTAIV.exe directly and brings the window to the front.

  The trainer does not start separately - it sits as an .asi in the plugins
  folder and is loaded by the ASI loader when the game starts. In game, F7 opens
  the menu.

.PARAMETER Log
  Follows the trainer log file while the game runs.

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
if (-not (Test-Path $exe)) { throw "GTAIV.exe not found under $GamePath" }

if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    Write-Host "The game is already running." -ForegroundColor Yellow
    exit 0
}

Write-Host "Starting GTA IV ..." -ForegroundColor Cyan
$game = Start-Process -FilePath $exe -WorkingDirectory $GamePath -PassThru

# Wait for the window instead of blindly bringing it forward - before that there
# is none.
foreach ($attempt in 1..30) {
    Start-Sleep -Seconds 2
    $proc = Get-Process -Id $game.Id -ErrorAction SilentlyContinue
    if (-not $proc) { throw "The game exited. Check the trainer log." }
    if ($proc.MainWindowHandle -ne 0) { break }
}

$sig = '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);'
$fg = Add-Type -MemberDefinition $sig -Name Foreground -Namespace MlivPlay -PassThru
$proc = Get-Process -Id $game.Id -ErrorAction SilentlyContinue
if ($proc -and $proc.MainWindowHandle -ne 0) { $fg::SetForegroundWindow($proc.MainWindowHandle) | Out-Null }

Write-Host "Running. F7 opens the trainer menu." -ForegroundColor Green

if (-not $Log) { exit 0 }

# The log lives wherever the trainer was allowed to create it - next to the DLL,
# or under LOCALAPPDATA when the game directory was not writable.
$candidates = @(
    (Join-Path $GamePath "plugins\ModlauncherIV-Trainer.log"),
    (Join-Path $env:LOCALAPPDATA "ModlauncherIV\Trainer.log")
)

$logFile = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $logFile) {
    Write-Host "No trainer log file found - is the ASI loading at all?" -ForegroundColor Yellow
    exit 0
}

Write-Host "`nLog: $logFile  (Ctrl+C stops following, not the game)`n" -ForegroundColor Cyan
Get-Content -LiteralPath $logFile -Wait -Tail 50
