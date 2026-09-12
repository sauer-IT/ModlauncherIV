<#
.SYNOPSIS
  Fetches the Visual C++ 2005 runtime from Microsoft and makes it shippable.

.DESCRIPTION
  GTA IV 1.0.7.0 does not start without the VC++ 2005 runtime. Windows then
  reports only that the side-by-side configuration is invalid, which says
  nothing about the actual cause.

  This was the one step a stranger could not get past: the recipe had no source
  at all and expected the file to be supplied by hand. Nobody who just wants to
  play gets through that.

  So the runtime is fetched from Microsoft, as an .exe with a valid Authenticode
  signature, pinned to a checksum. Nothing here is taken from a modding archive
  of unclear origin.

  There is one catch, and it is the whole reason this script exists.

  GTAIV.exe asks for Microsoft.VC80.CRT 8.0.50727.42 and Microsoft.VC80.ATL
  8.0.50727.762. Microsoft no longer ships either: every current download - the
  SP1 page included - serves 8.0.50727.6195, the version from the MS11-025
  security update. And Windows does not accept it. Measured against the real
  GTAIV.exe, with the assembly folders next to it and nothing else changed:

      no folders at all               side-by-side error
      Microsoft 6195, as shipped      side-by-side error
      6195 binaries, manifest 762     starts

  For a private, app-local assembly, Windows binds on the identity in the
  manifest next to the binary - not on the version resource of the DLL, and not
  on the hash attributes in the manifest either. Those attributes do not even
  match in Microsoft's own signed package; they are not checked.

  So the manifest declares the identity the game asks for, and the binaries are
  Microsoft's current ones. That is what a publisher policy does system-wide,
  done app-locally instead: the game gets the runtime it wants, and it gets the
  serviced one rather than the unpatched 2007 build that is otherwise passed
  around. The two manifests are the only files this project authors here.

  Microsoft has replaced the file behind that URL before - in 2024, in place. If
  they do it again the checksum will not match and this script stops. That is
  the point: it is better to notice than to ship something nobody looked at.

.PARAMETER KeepDownload
  Keeps the downloaded installer in the cache instead of deleting it. Useful
  when the extraction needs looking at.

.EXAMPLE
  .\scripts\pack-vc80.ps1
#>
[CmdletBinding()]
param(
    [switch] $KeepDownload
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# The redistributable, pinned.
#
# 2.7 MB, Authenticode-signed by Microsoft Corporation. The signature is checked
# below as well - a checksum only says the bytes are the ones we saw last time,
# not that Microsoft made them.
$url    = "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x86.EXE"
$sha256 = "8648c5fc29c44b9112fe52f9a33f80e7fc42d10f3b5b42b2121542a13e44adfd"

# The identity the game asks for. See the comment at the top for why this is not
# the version of the binaries.
$identity = "8.0.50727.762"

$work    = Join-Path $root "artifacts\vc80"
$bundled = Join-Path $root "src\Launcher.App\bundled"

# ----------------------------------------------------------------- 1. Fetching

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null

$installer = Join-Path $work "vcredist_x86.exe"

Write-Host "Fetching the VC++ 2005 runtime from Microsoft ..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing

$actual = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $sha256) {
    throw @"
The installer does not match the pinned checksum.
  expected $sha256
  got      $actual

Microsoft has replaced the file behind that URL before. Check what is there now,
confirm it is theirs, and only then update the checksum in this script.
"@
}

$signature = Get-AuthenticodeSignature $installer
if ($signature.Status -ne "Valid") {
    throw "The installer's signature is not valid: $($signature.Status)"
}
if ($signature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
    throw "The installer is not signed by Microsoft: $($signature.SignerCertificate.Subject)"
}

Write-Host "  checksum and Microsoft signature verified" -ForegroundColor Green

# --------------------------------------------------------------- 2. Extracting
#
# Two steps, because the .exe is a self-extracting shell around an MSI: the exe
# gives up the .msi and its .cab, and an administrative install unpacks the
# files out of those without installing anything on this machine.

$stage = Join-Path $work "stage"
$admin = Join-Path $work "admin"
New-Item -ItemType Directory -Path $stage, $admin -Force | Out-Null

& $installer /q /c /T:$stage | Out-Null

$msi = Join-Path $stage "vcredist.msi"
if (-not (Test-Path $msi)) { throw "No vcredist.msi in the extracted installer." }

$p = Start-Process msiexec.exe -Wait -PassThru `
     -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$admin`""
if ($p.ExitCode -ne 0) { throw "The administrative install failed (exit code $($p.ExitCode))." }

# The payload sits under winsxs in folders with generated names, so the files are
# found by name rather than by path - the names change between releases.
#
# Restricted to winsxs on purpose. The package also unpacks copies into
# system32, and among them an "Ansi" build of ATL80.dll that is bigger than the
# real one and is not the file the manifest describes. Picking by size, or
# searching the whole tree, gets that one.
function Find-One([string] $name) {
    $hit = Get-ChildItem $admin -Recurse -File -Filter $name |
           Where-Object { $_.FullName -like "*\winsxs\*" -and $_.FullName -notlike "*\Policies\*" } |
           Select-Object -First 1

    if (-not $hit) { throw "$name not found under winsxs in the extracted package." }
    return $hit.FullName
}

$assemblies = @(
    @{ Name = "Microsoft.VC80.CRT"; Files = @("msvcr80.dll", "msvcp80.dll", "msvcm80.dll") },
    @{ Name = "Microsoft.VC80.ATL"; Files = @("ATL80.dll") }
)

# --------------------------------------------------------------- 3. Assembling

New-Item -ItemType Directory -Path $bundled -Force | Out-Null

$sources = @()
$steps   = @()

foreach ($assembly in $assemblies) {
    $name = $assembly.Name

    $steps += "    { ""type"": ""ensureDirectory"", ""target"": ""$name"" }"

    # The manifest. Microsoft's own, with one attribute changed.
    # The manifests are stored under their SxS name, which carries an
    # architecture prefix and the public key token: x86_Microsoft.VC80.CRT_...
    $manifestSource = Find-One "*$($name)_*.manifest"
    $text = Get-Content $manifestSource -Raw
    $text = $text -replace 'version="8\.0\.50727\.\d+"', "version=""$identity"""

    if ($text -notmatch [regex]::Escape("version=""$identity""")) {
        throw "The version in $name's manifest could not be rewritten - has the format changed?"
    }

    $manifestName = "$name.manifest"
    $manifestPath = Join-Path $bundled $manifestName
    [System.IO.File]::WriteAllText($manifestPath, $text, (New-Object System.Text.UTF8Encoding($false)))

    foreach ($file in (@($manifestName) + $assembly.Files)) {
        if ($file -ne $manifestName) {
            Copy-Item (Find-One $file) (Join-Path $bundled $file) -Force
        }

        $path = Join-Path $bundled $file
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
        $size = (Get-Item $path).Length

        Write-Host ("  {0,-30} {1,9:N0} bytes" -f "$name\$file", $size)

        $note = if ($file -eq $manifestName) {
            "Microsoft's own manifest from the same package, with the declared version set to $identity - the identity GTAIV.exe asks for. Windows binds a private assembly on this identity; 8.0.50727.6195 as shipped is refused."
        } else {
            "Verbatim from Microsoft's signed vcredist_x86.exe (8.0.50727.6195, MS11-025)."
        }

        $sources += @"
    {
      "id": "$($file.ToLower().Replace('.', '-'))",
      "fileName": "$file",
      "sha256": "$hash",
      "sizeBytes": $size,
      "urls": [],
      "note": "$note"
    }
"@

        # Two backslashes: JSON reads them as the one the path needs.
        $steps += "    { ""type"": ""copyFile"", ""source"": ""$file"", ""target"": ""$name\\$file"" }"
    }
}

# ------------------------------------------------------------------ 4. Recipe
#
# The release number carries the binaries' real version plus the identity they
# are deployed under, so that the two never get confused when reading a ledger.

$version = "6195.0.0+id$($identity.Replace('.', ''))"

$recipe = @"
{
  "id": "vc80-runtime",
  "name": "Visual C++ 2005 runtime (next to the EXE)",
  "version": "$version",
  "game": "GtaIV",
  "description": "GTA IV 1.0.7.0 was built against the Visual C++ 2005 runtime and does not start without it - Windows then only reports that the side-by-side configuration is incorrect. It is missing on todays systems, and the downgrade package does not bring it. The files come from Microsofts signed redistributable and are placed next to the EXE rather than installed system-wide: that stays inside the game directory, is reversible, and needs no recipe step allowed to run foreign installers.",

  "appliesToVersions": [ "1.0.7.0", "1.0.8.0", "1.0.4.0" ],

  "sources": [
$($sources -join ",`n")
  ],

  "steps": [
$($steps -join ",`n")
  ]
}
"@

$recipePath = Join-Path $root "catalog\vc80-runtime.json"
Set-Content -Path $recipePath -Value $recipe -Encoding utf8

Write-Host "Recipe written: $recipePath" -ForegroundColor Green

if (-not $KeepDownload) { Remove-Item $work -Recurse -Force }

Write-Host ""
Write-Host "The catalog has to be signed again - pack-trainer.ps1 does that at the end." -ForegroundColor DarkGray
