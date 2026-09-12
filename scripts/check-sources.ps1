<#
.SYNOPSIS
  Asks every source in the catalog whether it is still there.

.DESCRIPTION
  Each recipe names files by URL, size and SHA-256, and refuses anything that
  does not match. That is the right way round, but it means a source which has
  been deleted, moved or silently re-uploaded turns into a failed installation
  on somebody else's machine, at the worst moment, with no warning here.

  So this asks, without downloading anything: does the URL answer, and does the
  size it announces match what the recipe expects? A different size is a
  different file - the checksum would refuse it anyway, and this says so before
  a tester spends a gigabyte finding out.

  What it cannot see is a file of exactly the same size with different content.
  -Deep downloads everything and hashes it, which is over two gigabytes and the
  reason it is not the default.

  Sources without a URL are listed, not judged: those are the three from Nexus,
  which are supplied by hand on purpose.

.PARAMETER Deep
  Download every file and check the SHA-256. Slow, thorough, over 2 GB.

.PARAMETER Catalog
  Recipe directory. Defaults to the catalog in this repository.

.EXAMPLE
  .\scripts\check-sources.ps1
  .\scripts\check-sources.ps1 -Deep
#>
[CmdletBinding()]
param(
    [switch] $Deep,
    [string] $Catalog = (Join-Path (Split-Path -Parent $PSScriptRoot) "catalog")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path $Catalog)) { throw "No catalog at $Catalog" }

# Same as the launcher sends. GitHub serves release assets to anything, but
# some hosts answer a request without a user agent differently or not at all.
$headers = @{ "User-Agent" = "ModlauncherIV/1.0 (source check)" }

$script:problems = 0
$script:checked = 0
$script:byHand = 0

# What the launcher carries inside itself - its own trainer and the VC++ 2005
# runtime. Those have no URL and need none.
$shippedRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "src\Launcher.App\bundled"

function Fail($text) { Write-Host "    $text" -ForegroundColor Red; $script:problems++ }
function Note($text) { Write-Host "    $text" -ForegroundColor DarkGray }
function Good($text) { Write-Host "    $text" -ForegroundColor Green }

$files = Get-ChildItem $Catalog -Filter *.json -Recurse |
    Where-Object { $_.Name -ne "index.json" } |
    Sort-Object FullName

foreach ($file in $files) {
    $recipe = Get-Content $file.FullName -Raw | ConvertFrom-Json

    # The tools in catalog\tools have a shape of their own: one installer, not a
    # list of sources. Both end up in the same check.
    $sources = @()
    if ($recipe.sources) {
        $sources = $recipe.sources
    } elseif ($recipe.installer) {
        $sources = @($recipe.installer)
    }

    if ($sources.Count -eq 0) { continue }

    Write-Host ""
    Write-Host "$($recipe.id)" -ForegroundColor Cyan

    foreach ($source in $sources) {
        $urls = @($source.urls | Where-Object { $_ })
        $size = [int64] $source.sizeBytes

        if ($urls.Count -eq 0) {
            # Two quite different things look the same from here: a file the
            # launcher carries itself, and one only Nexus has. The first can be
            # checked right now, and is worth checking - a shipped file whose
            # checksum drifted away from its recipe makes an EXE that refuses
            # its own payload, which has happened before.
            $shipped = Join-Path $shippedRoot $source.fileName

            if (Test-Path $shipped) {
                $script:checked++
                $hash = (Get-FileHash $shipped -Algorithm SHA256).Hash.ToLower()

                if ($hash -ne $source.sha256.ToLower()) {
                    Fail "$($source.id): shipped with the launcher, but with the wrong checksum - $hash"
                } else {
                    Good "$($source.id): shipped with the launcher, checksum matches"
                }
            } else {
                $script:byHand++
                Note "$($source.id): supplied by hand, no source to check"
            }

            continue
        }

        $anyGood = $false

        foreach ($url in $urls) {
            $script:checked++
            $host_ = ([uri] $url).Host

            try {
                $response = Invoke-WebRequest -Uri $url -Method Head -Headers $headers `
                    -MaximumRedirection 5 -TimeoutSec 60 -UseBasicParsing
            }
            catch {
                Fail "$($source.id): $host_ does not deliver - $($_.Exception.Message)"
                continue
            }

            $announced = $response.Headers["Content-Length"]
            if ($announced -is [array]) { $announced = $announced[0] }

            if (-not $announced) {
                Note "$($source.id): $host_ answers but announces no size"
                $anyGood = $true
                continue
            }

            if ([int64] $announced -ne $size) {
                Fail "$($source.id): $host_ has $announced bytes, the recipe expects $size"
                continue
            }

            $anyGood = $true
            Good "$($source.id): $host_ ok, $('{0:N0}' -f $size) bytes"
        }

        if (-not $anyGood) {
            Fail "$($source.id): NOT ONE source works - this recipe cannot be installed"
        }

        if (-not $Deep -or -not $anyGood) { continue }

        # The size matching proves nothing about the content. Only for -Deep,
        # and into the temp folder, because this is about the bytes on the far
        # end rather than anything we want to keep.
        $temp = Join-Path ([IO.Path]::GetTempPath()) ("mliv-check-" + [guid]::NewGuid().ToString("N"))

        try {
            Invoke-WebRequest -Uri $urls[0] -OutFile $temp -Headers $headers -TimeoutSec 1800 -UseBasicParsing
            $hash = (Get-FileHash $temp -Algorithm SHA256).Hash.ToLower()

            if ($hash -ne $source.sha256.ToLower()) {
                Fail "$($source.id): the content has changed - $hash instead of $($source.sha256)"
            } else {
                Good "$($source.id): checksum still matches"
            }
        }
        catch {
            Fail "$($source.id): download failed - $($_.Exception.Message)"
        }
        finally {
            if (Test-Path $temp) { Remove-Item $temp -Force }
        }
    }
}

Write-Host ""
Write-Host ("-" * 60)
Write-Host " $script:checked URLs checked, $script:byHand supplied by hand, $script:problems problem(s)" `
    -ForegroundColor $(if ($script:problems) { "Red" } else { "Green" })
Write-Host ("-" * 60)
Write-Host ""

if (-not $Deep -and $script:problems -eq 0) {
    Write-Host "Sizes only. -Deep downloads everything and checks the checksums." -ForegroundColor DarkGray
}

exit $(if ($script:problems) { 1 } else { 0 })
