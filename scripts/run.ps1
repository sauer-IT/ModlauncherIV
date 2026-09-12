<#
.SYNOPSIS
  Baut die CLI und startet sie mit den uebergebenen Argumenten.

.DESCRIPTION
  Warum "publish" statt "build":

  Smart App Control (Windows 11) blockiert das Laden unsignierter Managed-DLLs.
  Ein normaler Build erzeugt mliv.exe plus mliv.dll; die exe darf starten, das
  Laden der dll wird von der CI-Richtlinie abgelehnt (Ereignis 3077, Policy
  {0283ac0f-fff1-49ae-ada1-8a933130cad6}).

  Als Single-File-Publish gibt es keine separate Managed-DLL mehr, die geladen
  werden muesste — damit greift der Block nicht. Framework-abhaengig bleibt das
  Ergebnis klein (~235 KB) und der Durchlauf dauert rund 1,5 Sekunden.

.EXAMPLE
  .\scripts\run.ps1 detect
  .\scripts\run.ps1 detect --path "C:\Program Files\Rockstar Games\Grand Theft Auto IV"
  .\scripts\run.ps1 detect --json
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\fd"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet nicht gefunden. .NET 10 SDK installieren." }

& $dotnet publish (Join-Path $root "src\Launcher.Cli") `
    -c Debug -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o $out --nologo -v q

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Signieren gehoert in jeden Durchlauf, nicht nur ins Release: so faellt ein
# kaputter Signierschritt sofort auf und nicht erst beim Ausliefern.
& (Join-Path $PSScriptRoot "sign.ps1") -Path (Join-Path $out "mliv.exe") | Out-Null

& (Join-Path $out "mliv.exe") @Arguments
exit $LASTEXITCODE
