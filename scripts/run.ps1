<#
.SYNOPSIS
  Builds the CLI and runs it with the given arguments.

.DESCRIPTION
  Why "publish" rather than "build":

  Smart App Control (Windows 11) blocks loading unsigned managed DLLs. A normal
  build produces mliv.exe plus mliv.dll; the exe is allowed to start, but
  loading the dll gets rejected by the code integrity policy (event 3077, policy
  {0283ac0f-fff1-49ae-ada1-8a933130cad6}).

  As a single-file publish there is no separate managed DLL left to load - so
  the block does not apply. Framework-dependent, the result stays small
  (~235 KB) and a run takes about 1.5 seconds.

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
if (-not (Test-Path $dotnet)) { throw "dotnet not found. Install the .NET 10 SDK." }

& $dotnet publish (Join-Path $root "src\Launcher.Cli") `
    -c Debug -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o $out --nologo -v q

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Signing belongs in every run, not only in a release: that way a broken signing
# step shows up immediately rather than at shipping time.
& (Join-Path $PSScriptRoot "sign.ps1") -Path (Join-Path $out "mliv.exe") | Out-Null

& (Join-Path $out "mliv.exe") @Arguments
exit $LASTEXITCODE
