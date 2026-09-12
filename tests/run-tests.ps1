<#
.SYNOPSIS
  Tests the recipe pipeline against fake game directories.

.DESCRIPTION
  No real installation is touched. The fixtures are rebuilt on every run so
  that the tests stay independent of one another.

  The most important test is the rollback one: a recipe whose second step
  fails has to undo the first step. That property is the reason the whole
  pipeline exists.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $PSScriptRoot "work"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

# A running GTA IV makes every pre-flight block - the tests would then fail in
# batches without anything being wrong with the code. Better to stop once here
# with a clear message than to produce four puzzling FAILs further down.
if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    Write-Host "GTA IV is running. Close it first, otherwise every pre-flight blocks." -ForegroundColor Red
    exit 2
}

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

    # Under Windows PowerShell 5.1 "Stop" turns every stderr line of a native
    # program into a terminating NativeCommandError. Our tests are checking
    # exactly the failure cases, so deliberately give way here.
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

# ------------------------------------------------------------------- Setup

Write-Host "`n== Building ==" -ForegroundColor Cyan
& $dotnet publish (Join-Path $root "src\Launcher.Cli") -c Debug -r win-x64 `
    --self-contained false -p:PublishSingleFile=true -o (Join-Path $root "artifacts\fd") `
    --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Join-Path $root "artifacts\fd\mliv.exe"

Write-Host "`n== Building fixtures ==" -ForegroundColor Cyan
if (Test-Path $work) { Remove-Item $work -Recurse -Force }

$game = Join-Path $work "game-107"
$cache = Join-Path $work "cache"
$catalog = Join-Path $work "catalog"
New-Item -ItemType Directory -Path $game, $cache, $catalog -Force | Out-Null

# A fake game. GTAIV.exe has to be there or the folder is not recognised.
Set-Content -Path (Join-Path $game "GTAIV.exe") -Value "fake game file" -NoNewline -Encoding utf8
Set-Content -Path (Join-Path $game "vorhanden.txt") -Value "original content" -NoNewline -Encoding utf8
New-Item -ItemType Directory -Path (Join-Path $game "belegt") -Force | Out-Null

# Two source files with real checksums.
Set-Content -Path (Join-Path $cache "xliveless.dll") -Value "stand-in for the GFWL stub" -NoNewline -Encoding utf8
Set-Content -Path (Join-Path $cache "loader.dll") -Value "stand-in for the ASI loader" -NoNewline -Encoding utf8

function Get-Sha([string] $p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() }
function Get-Size([string] $p) { (Get-Item $p).Length }

$xliveHash = Get-Sha (Join-Path $cache "xliveless.dll")
$xliveSize = Get-Size (Join-Path $cache "xliveless.dll")
$loaderHash = Get-Sha (Join-Path $cache "loader.dll")
$loaderSize = Get-Size (Join-Path $cache "loader.dll")

# Recipe 1: goes through cleanly.
@"
{
  "id": "test-ok",
  "name": "Test recipe, working",
  "version": "1.0.0",
  "game": "GtaIV",
  "description": "Copies two files into the game directory.",
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

# Recipe 2: checksum deliberately wrong -> has to block in pre-flight.
@"
{
  "id": "test-badhash",
  "name": "Test recipe, wrong checksum",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "xlive.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-badhash.json") -Encoding utf8

# Recipe 3: path points out of the game directory -> has to be rejected.
@"
{
  "id": "test-escape",
  "name": "Test recipe, path escape",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "..\\..\\entkommen.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-escape.json") -Encoding utf8

# Recipe 4: step 1 succeeds, step 2 fails (the target is a directory).
# Pre-flight cannot see that -> forces a real rollback.
@"
{
  "id": "test-rollback",
  "name": "Test recipe, fails on the second step",
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

# Clean up state from earlier runs.
$installs = Join-Path $env:LOCALAPPDATA "ModlauncherIV\installs"
if (Test-Path $installs) {
    Get-ChildItem $installs -Directory | Where-Object { $_.Name -like "game-107-*" } |
        Remove-Item -Recurse -Force
}

# By default the catalog is only loaded when signed. The recipe tests work
# unsigned on purpose; the signature gets its own block.
$common = @("--path", $game, "--catalog", $catalog, "--cache", $cache, "--allow-unsigned")

# Sources for the acquisition tests.
$fetchCache = Join-Path $work "fetch-cache"
New-Item -ItemType Directory -Path $fetchCache -Force | Out-Null
Copy-Item (Join-Path $cache "xliveless.dll") (Join-Path $fetchCache "xliveless.dll")

@"
{
  "id": "test-fetch-present",
  "name": "Test recipe, file already present",
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
  "name": "Test recipe, file missing with no source",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "gross", "fileName": "downgrade-paket.zip", "sha256": "1111111111111111111111111111111111111111111111111111111111111111", "sizeBytes": 1234567, "urls": [], "note": "Aus dem Community-Downgrader." }
  ],
  "steps": [ { "type": "extractArchive", "archive": "downgrade-paket.zip", "target": "" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-fetch-missing.json") -Encoding utf8

# Two edges in the version graph: 1.2.0.59 -> 1.0.8.0 -> 1.0.7.0.
@"
{
  "id": "test-down-ce-108",
  "name": "Test recipe, CE to 1.0.8.0",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.2.0.59", "1.2.0.43" ],
  "producesVersion": "1.0.8.0",
  "steps": [ { "type": "ensureDirectory", "target": "downgrade-marker" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-down-ce-108.json") -Encoding utf8

@"
{
  "id": "test-down-108-107",
  "name": "Test recipe, 1.0.8.0 to 1.0.7.0",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.8.0" ],
  "producesVersion": "1.0.7.0",
  "steps": [ { "type": "ensureDirectory", "target": "downgrade-marker-2" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-down-108-107.json") -Encoding utf8

# Recipes for the journey planner. test-j-top depends on test-j-base, and both
# only apply to 1.0.7.0 - so only after the downgrade.
@"
{
  "id": "test-j-base",
  "name": "Test recipe, foundation",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-base" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-base.json") -Encoding utf8

@"
{
  "id": "test-j-top",
  "name": "Test recipe, builds on the foundation",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "requires": [ "test-j-base" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-top" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-top.json") -Encoding utf8

@"
{
  "id": "test-j-streit",
  "name": "Test recipe, conflicts with the foundation",
  "version": "1.0.0",
  "game": "GtaIV",
  "appliesToVersions": [ "1.0.7.0" ],
  "conflictsWith": [ "test-j-base" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-streit" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-streit.json") -Encoding utf8

# Two recipes that require each other. Without a cycle check the resolution
# either never ends or quietly returns a wrong order.
@"
{
  "id": "test-j-ring-a",
  "name": "Test recipe, cycle A",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-j-ring-b" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-ring-a" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-ring-a.json") -Encoding utf8

@"
{
  "id": "test-j-ring-b",
  "name": "Test recipe, cycle B",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-j-ring-a" ],
  "steps": [ { "type": "ensureDirectory", "target": "j-ring-b" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-j-ring-b.json") -Encoding utf8

# A fake Steam layout: game in steamapps/common, manifest beside it.
$steamRoot = Join-Path $work "steam"
$steamGame = Join-Path $steamRoot "steamapps\common\Grand Theft Auto IV"
New-Item -ItemType Directory -Path $steamGame -Force | Out-Null
Set-Content -Path (Join-Path $steamGame "GTAIV.exe") -Value "fake game file" -NoNewline -Encoding utf8
$acf = Join-Path $steamRoot "steamapps\appmanifest_12210.acf"
@'
"AppState"
{
	"appid"		"12210"
	"name"		"Grand Theft Auto IV"
	"AutoUpdateBehavior"		"0"
	"installdir"		"Grand Theft Auto IV"
}
'@ | Set-Content -Path $acf -Encoding utf8

# ------------------------------------------------------------------- Tests

Write-Host "`n== Catalog ==" -ForegroundColor Cyan
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--allow-unsigned")
Assert ($r.ExitCode -eq 0) "catalog: exit code 0"
Assert ($r.Output -match "test-ok") "catalog: lists test-ok"
Assert ($r.Output -match "13 recipe") "catalog: finds all thirteen"
Assert ($r.Output -match "NOT checked against a signature") "catalog: warns about the missing signature check"

Write-Host "`n== Checksum protection ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("plan", "test-badhash") + $common)
Assert ($r.ExitCode -eq 3) "badhash: blocked (exit code 3)"
Assert ($r.Output -match "Checksum does not match") "badhash: names the checksum as the reason"

Write-Host "`n== Path escape ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("plan", "test-escape") + $common)
Assert ($r.ExitCode -eq 3) "escape: blocked (exit code 3)"
Assert ($r.Output -match "points outside") "escape: names the path escape"
Assert (-not (Test-Path (Join-Path $work "entkommen.dll"))) "escape: nothing written outside"

Write-Host "`n== Dry run changes nothing ==" -ForegroundColor Cyan
$before = (Get-ChildItem $game -Recurse -File).Count
$r = Invoke-Mliv (@("plan", "test-ok") + $common)
$after = (Get-ChildItem $game -Recurse -File).Count
Assert ($r.ExitCode -eq 0) "plan: exit code 0"
Assert ($before -eq $after) "plan: file count unchanged ($before)"
Assert ($r.Output -match "xlive\.dll") "plan: names the target file"

Write-Host "`n== Executing ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("apply", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "apply: exit code 0"
Assert (Test-Path (Join-Path $game "xlive.dll")) "apply: xlive.dll created"
Assert (Test-Path (Join-Path $game "dsound.dll")) "apply: dsound.dll created"
Assert ((Get-Sha (Join-Path $game "xlive.dll")) -eq $xliveHash) "apply: content matches the source"

Write-Host "`n== Ledger ==" -ForegroundColor Cyan
$r = Invoke-Mliv (@("status") + $common)
Assert ($r.ExitCode -eq 0) "status: exit code 0"
Assert ($r.Output -match "test-ok") "status: lists test-ok"
Assert ($r.Output -match "Snapshot") "status: names the snapshot"

Write-Host "`n== Rollback ==" -ForegroundColor Cyan
$originalContent = Get-Content (Join-Path $game "vorhanden.txt") -Raw
$r = Invoke-Mliv (@("apply", "test-rollback", "--yes") + $common)
$restoredContent = Get-Content (Join-Path $game "vorhanden.txt") -Raw
Assert ($r.ExitCode -eq 5) "rollback: reports failure (exit code 5)"
Assert ($r.Output -match "was restored") "rollback: reports the restore"
Assert ($originalContent -eq $restoredContent) "rollback: the existing file has its old content"

$statusAfter = Invoke-Mliv (@("status") + $common)
Assert (-not ($statusAfter.Output -match "test-rollback")) "rollback: no ledger entry for the failure"

# ------------------------------------------------------------------ verify

Write-Host "`n== Counter-check ==" -ForegroundColor Cyan

$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.ExitCode -eq 0) "verify: an unchanged installation is fine"
Assert ($r.Output -match "Everything unchanged") "verify: reports everything unchanged"

# Change an installed file - exactly what a store update does.
Set-Content -Path (Join-Path $game "xlive.dll") -Value "overwritten by the store" -NoNewline -Encoding utf8
$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.ExitCode -eq 3) "verify: a changed file is detected"
Assert ($r.Output -match "changed\s+xlive\.dll") "verify: names the affected file"
Assert ($r.Output -match "test-ok") "verify: names the owning recipe"

# And delete one.
Remove-Item (Join-Path $game "dsound.dll") -Force
$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.Output -match "missing\s+dsound\.dll") "verify: a missing file is detected"

# ------------------------------------------------------------------- route

Write-Host "`n== Version graph ==" -ForegroundColor Cyan
$routeArgs = @("--path", $game, "--catalog", $catalog, "--allow-unsigned", "--assume-version", "1.2.0.59")

$r = Invoke-Mliv (@("route") + $routeArgs)
Assert ($r.ExitCode -eq 0) "route: exit code 0"
Assert ($r.Output -match "1\.0\.8\.0") "route: 1.0.8.0 is reachable"
Assert ($r.Output -match "1\.0\.7\.0") "route: 1.0.7.0 is reachable"

$r = Invoke-Mliv (@("route", "1.0.7.0") + $routeArgs)
Assert ($r.ExitCode -eq 0) "route: path to 1.0.7.0 found"
Assert ($r.Output -match "test-down-ce-108") "route: first step is the CE edge"
Assert ($r.Output -match "test-down-108-107") "route: second step is the 1.0.8.0 edge"
Assert ($r.Output -match "2 recipe") "route: two steps"

# Regression: FileVersionInfo reports either "1.0.7.0" or "1, 0, 7, 0" depending
# on the binary. Comparing both forms unexamined made verify report a patch-back
# where there was none - devaluing the very warning this is about.
$r = Invoke-Mliv @("route", "--path", $game, "--catalog", $catalog, "--allow-unsigned",
                   "--assume-version", "1, 2, 0, 59")
Assert ($r.Output -match "1\.0\.7\.0") "normalisation: the comma form is recognised as 1.2.0.59"

$r = Invoke-Mliv @("route", "1.0.7.0", "--path", $game, "--catalog", $catalog, "--allow-unsigned",
                   "--assume-version", "1, 0, 7, 0")
Assert ($r.ExitCode -eq 0) "normalisation: the comma form of the target is recognised"
Assert ($r.Output -match "already on") "normalisation: no path needed, the version already matches"

$r = Invoke-Mliv (@("route", "9.9.9.9") + $routeArgs)
Assert ($r.ExitCode -eq 1) "route: an unreachable version reports failure"
Assert ($r.Output -match "No path") "route: says there is no path"

# ----------------------------------------------------------------- journey

Write-Host "`n== Journey planning ==" -ForegroundColor Cyan
$jArgs = @("--path", $game, "--catalog", $catalog, "--allow-unsigned")

# The heart of the wizard: test-j-top only applies to 1.0.7.0. Somebody on
# 1.2.0.59 still gets it - because after the downgrade it fits. Checked against
# the current version, the whole path would be impossible.
$r = Invoke-Mliv (@("journey", "test-j-top", "--assume-version", "1.2.0.59", "--target", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 0) "journey: the path through the downgrade is possible"
Assert ($r.Output -match "test-down-ce-108")  "journey: the first edge is included"
Assert ($r.Output -match "test-down-108-107") "journey: the second edge is included"
Assert ($r.Output -match "test-j-base")       "journey: the dependency was added"
Assert ($r.Output -match "test-j-top")        "journey: the wanted recipe is in there"
Assert ($r.Output -match "Open: 4 of 4")    "journey: four open steps"

# Order: the version change has to come before everything else, otherwise the
# downgrade overwrites the files that were just installed.
$posDown = $r.Output.IndexOf("test-down-ce-108")
$posBase = $r.Output.IndexOf("test-j-base")
$posTop  = $r.Output.IndexOf("test-j-top")
Assert ($posDown -lt $posBase) "journey: the downgrade comes before the recipes"
Assert ($posBase -lt $posTop)  "journey: a dependency comes before what needs it"

# Without the downgrade test-j-top does not fit - that has to be noticed.
$r = Invoke-Mliv (@("journey", "test-j-top", "--assume-version", "1.2.0.59") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: blocked without the downgrade"
Assert ($r.Output -match "does not fit version") "journey: names the unsuitable version"

$r = Invoke-Mliv (@("journey", "test-j-ring-a") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: a dependency cycle blocks"
Assert ($r.Output -match "require one another") "journey: names the cycle"
Assert ($r.Output -match "test-j-ring-a -> test-j-ring-b") "journey: shows the cycle as a chain"

$r = Invoke-Mliv (@("journey", "test-j-base,test-j-streit", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: a conflict blocks"
Assert ($r.Output -match "conflicts with") "journey: names the conflict"

$r = Invoke-Mliv (@("journey", "does-not-exist") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: an unknown recipe blocks"
Assert ($r.Output -match "is not in the catalog") "journey: says the recipe does not exist"

# Without a readable version there is no starting point - and thus no path.
$r = Invoke-Mliv (@("journey", "test-j-base", "--target", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: an unknown starting version blocks"
Assert ($r.Output -match "cannot be determined") "journey: says the version is missing"

# The missing version is the cause. Reporting it again per recipe as "does not
# fit (no version information)" would bury it.
Assert ($r.Output -notmatch "does not fit version") "journey: no follow-up message about the missing version"

# Even without a target version an unreadable version must not slip through
# silently - otherwise we would install unchecked.
$r = Invoke-Mliv (@("journey", "test-j-base") + $jArgs)
Assert ($r.ExitCode -eq 3) "journey: an unknown version blocks even without a target"

$r = Invoke-Mliv (@("journey", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.ExitCode -eq 0) "journey: with nothing wanted there is nothing to do"
Assert ($r.Output -match "nothing to do") "journey: and says so"

# An installed recipe at a newer release. Without comparing versions the planner
# would consider it done, and every user would be stuck on the release they
# first installed.
$r = Invoke-Mliv (@("apply", "test-j-base", "--yes", "--assume-version", "1.0.7.0", "--cache", $cache) + $jArgs)
Assert ($r.ExitCode -eq 0) "update: recipe installed at release 1.0.0"

$r = Invoke-Mliv (@("journey", "test-j-base", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.Output -match "already there") "update: the same release counts as done"
Assert ($r.Output -match "Open: 0") "update: and is not an open step"

(Get-Content (Join-Path $catalog "test-j-base.json") -Raw).Replace('"version": "1.0.0"', '"version": "2.0.0"') |
    Set-Content (Join-Path $catalog "test-j-base.json") -Encoding utf8 -NoNewline

$r = Invoke-Mliv (@("journey", "test-j-base", "--assume-version", "1.0.7.0") + $jArgs)
Assert ($r.Output -match "update 1.0.0 -> 2.0.0") "update: a newer release is noticed"
Assert ($r.Output -match "Open: 1") "update: and becomes an open step"

# Reset so the following sections find the same catalog.
(Get-Content (Join-Path $catalog "test-j-base.json") -Raw).Replace('"version": "2.0.0"', '"version": "1.0.0"') |
    Set-Content (Join-Path $catalog "test-j-base.json") -Encoding utf8 -NoNewline

# test-j-base is installed and is for 1.0.7.0. A journey that ends on 1.0.8.0
# leaves it exactly where it is - installed, listed, and not loading - and
# nothing used to say so. It is not a blocker: changing version anyway is a
# legitimate wish, and taking somebody's mods out uninvited is not the
# planner's decision.
$r = Invoke-Mliv (@("journey", "--assume-version", "1.2.0.59", "--target", "1.0.8.0") + $jArgs)
Assert ($r.Output -match "LEFT BEHIND") "stranded: a version change names what it leaves behind"
Assert ($r.Output -match "Test recipe, foundation") "stranded: by name"
Assert ($r.Output -match "made for 1\.0\.7\.0") "stranded: and says what it was made for"
Assert ($r.ExitCode -ne 3) "stranded: but does not block the journey"

# The same journey back to the version it fits says nothing of the sort.
$r = Invoke-Mliv (@("journey", "--assume-version", "1.2.0.59", "--target", "1.0.7.0") + $jArgs)
Assert (-not ($r.Output -match "LEFT BEHIND")) "stranded: nothing is left behind when the version still fits"

$r = Invoke-Mliv (@("remove", "test-j-base", "--yes") + $jArgs)
Assert ($r.ExitCode -eq 0) "update: test recipe removed again"

# ------------------------------------------------------------------- guard

Write-Host "`n== Update guard ==" -ForegroundColor Cyan

$r = Invoke-Mliv @("guard", "--path", $steamGame)
Assert ($r.ExitCode -eq 3) "guard: an open Steam installation is flagged"
Assert ($r.Output -match "Steam") "guard: recognises Steam by the manifest above the folder"
Assert ($r.Output -match "may update at any time") "guard: names the open state"

$r = Invoke-Mliv @("guard", "--path", $steamGame, "--apply")
Assert ($r.ExitCode -eq 0) "guard --apply: exit code 0"
Assert (Test-Path "$acf.mliv-backup") "guard --apply: backup of the manifest created"
Assert ((Get-Content $acf -Raw) -match '"AutoUpdateBehavior"\s*"1"') "guard --apply: switch set"

$r = Invoke-Mliv @("guard", "--path", $steamGame)
Assert ($r.ExitCode -eq 0) "guard: a locked installation is fine"
Assert ($r.Output -match "only updates on launch") "guard: reports the lock"

$r = Invoke-Mliv (@("guard") + $common)
Assert ($r.Output -match "origin of this installation is unknown") "guard: an unknown origin is reported as such"
Assert (-not ($r.Output -match "AutoUpdateBehavior")) "guard: no Steam switch for an unknown origin"

# ------------------------------------------------------------- Acquisition

Write-Host "`n== Acquisition ==" -ForegroundColor Cyan
$fetchArgs = @("--catalog", $catalog, "--cache", $fetchCache, "--allow-unsigned")

$r = Invoke-Mliv (@("fetch", "test-fetch-present") + $fetchArgs)
Assert ($r.ExitCode -eq 0) "fetch: an existing, correct file is accepted"
Assert ($r.Output -match "already there") "fetch: reports it as already present"

# Corrupt the file in the working directory -> has to be discarded.
Set-Content -Path (Join-Path $fetchCache "xliveless.dll") -Value "tampered" -NoNewline -Encoding utf8
$r = Invoke-Mliv (@("fetch", "test-fetch-present") + $fetchArgs)
Assert ($r.ExitCode -eq 5) "fetch: a wrong checksum in the cache is not accepted"
Assert ($r.Output -match "wrong checksum") "fetch: names the checksum as the reason"
Assert (-not (Test-Path (Join-Path $fetchCache "xliveless.dll"))) "fetch: the corrupted file was removed"

$r = Invoke-Mliv (@("fetch", "test-fetch-missing") + $fetchArgs)
Assert ($r.ExitCode -eq 5) "fetch: a missing file with no source reports failure"
Assert ($r.Output -match "TO BE SUPPLIED BY HAND") "fetch: gives instructions for supplying it by hand"
Assert ($r.Output -match "1111111111") "fetch: names the expected checksum"
Assert ($r.Output -match "Community-Downgrader") "fetch: passes on the note from the recipe"

# -------------------------------------------------------- Shipped payload

Write-Host "`n== Shipped payload ==" -ForegroundColor Cyan

# The launcher ships one file itself - its own trainer. It sits next to the
# program rather than online. It is checked anyway: the folder is writable by
# anyone with rights there, so being shipped is no bonus in trust.

$bundledDir = Join-Path $root "artifacts\fd\bundled"
New-Item -ItemType Directory -Path $bundledDir -Force | Out-Null
$bundledFile = Join-Path $bundledDir "mitgeliefert.dll"
Set-Content -Path $bundledFile -Value "stand-in for the trainer" -NoNewline -Encoding utf8

$bundledHash = Get-Sha $bundledFile
$bundledSize = Get-Size $bundledFile

@"
{
  "id": "test-bundled",
  "name": "Test recipe, shipped file",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "mit", "fileName": "mitgeliefert.dll", "sha256": "$bundledHash", "sizeBytes": $bundledSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "mitgeliefert.dll", "target": "mitgeliefert.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-bundled.json") -Encoding utf8

$bundledCache = Join-Path $work "bundled-cache"
New-Item -ItemType Directory -Path $bundledCache -Force | Out-Null
$bundledArgs = @("--catalog", $catalog, "--cache", $bundledCache, "--allow-unsigned")

$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 0) "shipped: the file is taken over"
Assert ($r.Output -match "shipped\s") "shipped: reported as shipped"
Assert (Test-Path (Join-Path $bundledCache "mitgeliefert.dll")) "shipped: lands in the working directory"
Assert ((Get-Sha (Join-Path $bundledCache "mitgeliefert.dll")) -eq $bundledHash) "shipped: the content matches"

# The actual point of the check: somebody swaps the shipped file. Without the
# checksum comparison arbitrary code would land in the game.
Remove-Item (Join-Path $bundledCache "mitgeliefert.dll") -Force
Set-Content -Path $bundledFile -Value "swapped" -NoNewline -Encoding utf8

$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 5) "shipped: a swapped file is rejected"
Assert ($r.Output -match "wrong checksum") "shipped: names the checksum as the reason"
Assert (-not (Test-Path (Join-Path $bundledCache "mitgeliefert.dll"))) "shipped: nothing reached the working directory"

Remove-Item $bundledFile -Force
$r = Invoke-Mliv (@("fetch", "test-bundled") + $bundledArgs)
Assert ($r.ExitCode -eq 5) "shipped: a missing file reports failure"

# -------------------------------------------------------- Download and mirrors

Write-Host "`n== Download and mirrors ==" -ForegroundColor Cyan

$port = 18734
$srvDir = Join-Path $work "server"
New-Item -ItemType Directory -Path $srvDir -Force | Out-Null

# Both files exactly the same length: otherwise the size check catches it first
# and the checksum rejection during the download would stay untested.
Set-Content (Join-Path $srvDir "good.bin") -Value "OK-payload-for-the-test" -NoNewline -Encoding ascii
Set-Content (Join-Path $srvDir "bad.bin")  -Value "XX-payload-for-the-test" -NoNewline -Encoding ascii

$goodHash = Get-Sha (Join-Path $srvDir "good.bin")
$goodSize = Get-Size (Join-Path $srvDir "good.bin")

# Three sources: 404, then wrong content, then the right one. Only if the
# acquirer survives both failures does it get to the third.
@"
{
  "id": "test-mirror",
  "name": "Test recipe, mirror chain",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "payload", "fileName": "payload.bin", "sha256": "$goodHash", "sizeBytes": $goodSize,
      "urls": [
        "http://localhost:$port/notthere.bin",
        "http://localhost:$port/bad.bin",
        "http://localhost:$port/good.bin"
      ] }
  ],
  "steps": [ { "type": "copyFile", "source": "payload.bin", "target": "payload.bin" } ]
}
"@ | Set-Content -Path (Join-Path $work "test-mirror.json") -Encoding utf8

# Depends on test-ok - for the dependency check during removal.
@"
{
  "id": "test-needs-ok",
  "name": "Test recipe, requires test-ok",
  "version": "1.0.0",
  "game": "GtaIV",
  "requires": [ "test-ok" ],
  "steps": [ { "type": "ensureDirectory", "target": "depends-on-it" } ]
}
"@ | Set-Content -Path (Join-Path $work "test-needs-ok.json") -Encoding utf8

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

# Wait for readiness rather than charging in blind.
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
    Write-Host "  SKIP  local HTTP server would not start (port $port taken, or ACL) - download tests skipped" -ForegroundColor Yellow
} else {
    $dlCache = Join-Path $work "dl-cache"
    New-Item -ItemType Directory -Path $dlCache -Force | Out-Null

    Copy-Item (Join-Path $work "test-mirror.json") (Join-Path $catalog "test-mirror.json")

    $r = Invoke-Mliv @("fetch", "test-mirror", "--catalog", $catalog, "--cache", $dlCache, "--allow-unsigned")
    Assert ($r.ExitCode -eq 0) "download: file acquired through the mirror chain"
    Assert (Test-Path (Join-Path $dlCache "payload.bin")) "download: the file is in the working directory"
    Assert ((Get-Sha (Join-Path $dlCache "payload.bin")) -eq $goodHash) "download: the content is the expected one"
    Assert ($r.Output -match "notthere\.bin") "download: names the first mirror that failed"
    Assert ($r.Output -match "bad\.bin") "download: names the second mirror with wrong content"
    Assert (-not (Test-Path (Join-Path $dlCache "payload.bin.part"))) "download: no .part leftovers"

    # Second run: nothing left to do, no second trip to the network needed.
    $r = Invoke-Mliv @("fetch", "test-mirror", "--catalog", $catalog, "--cache", $dlCache, "--allow-unsigned")
    Assert ($r.ExitCode -eq 0) "download: the second run succeeds"
    Assert ($r.Output -match "already there") "download: the second run does not download again"

    Remove-Item (Join-Path $catalog "test-mirror.json") -Force
}

Stop-Job $server -ErrorAction SilentlyContinue | Out-Null
Remove-Job $server -Force -ErrorAction SilentlyContinue | Out-Null

# ------------------------------------------------------------ Katalogsignatur

Write-Host "`n== Catalog signature ==" -ForegroundColor Cyan

$r = Invoke-Mliv @("catalog", "--catalog", $catalog)
Assert ($r.ExitCode -ne 0) "signature: an unsigned catalog is rejected without the flag"
Assert ($r.Output -match "is not trusted") "signature: names the missing trust as the reason"
Assert (-not ($r.Output -match "test-ok\s")) "signature: loads not a single recipe"

$keyFile = Join-Path $work "catalog-signing-key.pem"
$r = Invoke-Mliv @("catalog-key", "--key", $keyFile)
Assert ($r.ExitCode -eq 0) "catalog-key: exit code 0"
Assert (Test-Path $keyFile) "catalog-key: private key written"
Assert ($r.Output -match "EmbeddedPublicKey") "catalog-key: names the public part to paste in"

$publicKey = ([regex]::Match($r.Output, 'EmbeddedPublicKey = "([^"]+)"')).Groups[1].Value
Assert ($publicKey.Length -gt 40) "catalog-key: the public key is usable"

$r = Invoke-Mliv @("catalog-key", "--key", $keyFile)
Assert ($r.ExitCode -ne 0) "catalog-key: an existing key is not overwritten"

$r = Invoke-Mliv @("catalog-sign", "--catalog", $catalog, "--key", $keyFile)
Assert ($r.ExitCode -eq 0) "catalog-sign: exit code 0"
Assert (Test-Path (Join-Path $catalog "index.json")) "catalog-sign: index created"
Assert (Test-Path (Join-Path $catalog "index.json.sig")) "catalog-sign: signature created"

$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -eq 0) "signature: a signed catalog is accepted"
Assert ($r.Output -match "14 recipe") "signature: loads every recipe from the index"
Assert (-not ($r.Output -match "NOT checked against a signature")) "signature: no unsigned warning"

# Change a recipe file after signing. The index is signed, so the difference has
# to be noticed - and only this one file may drop out.
$tampered = Join-Path $catalog "test-ok.json"
(Get-Content $tampered -Raw).Replace('"target": "xlive.dll"', '"target": "boese.dll"') |
    Set-Content $tampered -Encoding utf8 -NoNewline
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -ne 0) "tampering: a changed recipe file is noticed"
Assert ($r.Output -match "differs from the signed index") "tampering: names the index comparison"
Assert (-not ($r.Output -match "boese\.dll")) "tampering: the tampered recipe is not loaded"
Assert ($r.Output -match "test-rollback") "tampering: unchanged recipes stay usable"

# Forge the signature itself.
Set-Content (Join-Path $catalog "index.json.sig") -Value ([Convert]::ToBase64String((1..64))) -Encoding utf8 -NoNewline
$r = Invoke-Mliv @("catalog", "--catalog", $catalog, "--public-key", $publicKey)
Assert ($r.ExitCode -ne 0) "tampering: a forged signature is rejected"
Assert ($r.Output -match "invalid|is not trusted") "tampering: names the signature as the reason"

# -------------------------------------------------------------------- remove

Write-Host "`n== Removal ==" -ForegroundColor Cyan

# A recipe that requires test-ok - so the dependency check applies.
Copy-Item (Join-Path $work "test-needs-ok.json") (Join-Path $catalog "test-needs-ok.json")
$r = Invoke-Mliv (@("apply", "test-needs-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "removal: dependent recipe installed"

# Remove test-ok while test-needs-ok depends on it -> has to be refused.
$r = Invoke-Mliv (@("remove", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 5) "removal: a depended-on recipe is not removed"
Assert ($r.Output -match "requires test-ok") "removal: names the dependent recipe"
Assert (Test-Path (Join-Path $game "xlive.dll")) "removal: nothing was touched"

# Without an argument and without --all: a hint instead of guessing.
$r = Invoke-Mliv (@("remove") + $common)
Assert ($r.ExitCode -eq 2) "removal: without an argument it does not guess"
Assert ($r.Output -match "remove --all") "removal: names the way to remove everything"

# Everything back out, newest first - that way the dependency resolves itself.
$r = Invoke-Mliv (@("remove", "--all", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "removal: --all runs through"
Assert (-not (Test-Path (Join-Path $game "xlive.dll"))) "removal: the newly created file is gone again"
Assert (-not (Test-Path (Join-Path $game "dsound.dll"))) "removal: the second new file is gone again"

# The file that already existed has to have its original content back -
# deleting alone would not bring it back.
Assert ((Get-Content (Join-Path $game "vorhanden.txt") -Raw) -eq $originalContent) `
    "removal: the pre-existing file has its old content back"

$r = Invoke-Mliv (@("status") + $common)
Assert (-not ($r.Output -match "test-ok")) "removal: the ledger is empty"

$r = Invoke-Mliv (@("remove", "test-ok", "--yes") + $common)
Assert ($r.ExitCode -eq 5) "removal: what is not installed cannot be removed"

# ------------------------------------------------ Empty folders after removal

Write-Host "`n== Empty folders after removal ==" -ForegroundColor Cyan

# A recipe that puts a file two folders deep. Taking it back has to take the
# folders with it - a texture pack removed this way used to leave a tree of
# empty directories behind, and the game directory then looks modded while
# holding nothing.

@"
{
  "id": "test-deep",
  "name": "Test recipe, writes into new folders",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [
    { "type": "copyFile", "source": "xliveless.dll", "target": "update\\deep\\inner\\thing.dll" },
    { "type": "copyFile", "source": "xliveless.dll", "target": "belegt\\thing.dll" }
  ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-deep.json") -Encoding utf8

$r = Invoke-Mliv (@("apply", "test-deep", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "folders: the recipe installed"
Assert (Test-Path (Join-Path $game "update\deep\inner\thing.dll")) "folders: the deep file is there"

$r = Invoke-Mliv (@("remove", "test-deep", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "folders: it was taken back"
Assert (-not (Test-Path (Join-Path $game "update\deep\inner\thing.dll"))) "folders: the file is gone"
Assert (-not (Test-Path (Join-Path $game "update\deep\inner"))) "folders: the innermost folder is gone"
Assert (-not (Test-Path (Join-Path $game "update"))) "folders: and so is the whole tree it created"

# A folder that was already there stays, even though it is empty again now.
# The snapshot knows the difference, and that difference is the whole rule.
Assert (Test-Path (Join-Path $game "belegt")) "folders: a folder that existed before is kept"

Remove-Item (Join-Path $catalog "test-deep.json") -Force

# ----------------------------------------------------- Leftovers on an update

Write-Host "`n== Leftovers on an update ==" -ForegroundColor Cyan

# A recipe that renames the file it installs between two releases. That is the
# case that used to leave the old one behind, and for an ASI it means the game
# loads both - the older one answering on the same key as the newer.

@"
{
  "id": "test-rename",
  "name": "Test recipe, renames its file",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "plugins\\old-name.asi" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-rename.json") -Encoding utf8

$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "leftovers: the first version installed"
Assert (Test-Path (Join-Path $game "plugins\old-name.asi")) "leftovers: the old file is there"

# Release two, same recipe, different file name.
@"
{
  "id": "test-rename",
  "name": "Test recipe, renames its file",
  "version": "2.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "xlive", "fileName": "xliveless.dll", "sha256": "$xliveHash", "sizeBytes": $xliveSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "xliveless.dll", "target": "plugins\\new-name.asi" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-rename.json") -Encoding utf8

# The dry run has to say so before anything happens, or the deletion would be a
# surprise: no recipe asked for it, it follows from what was installed before.
$r = Invoke-Mliv (@("plan", "test-rename") + $common)
Assert ($r.Output -match "LEFT OVER FROM THE PREVIOUS VERSION") "leftovers: the dry run announces the cleanup"
Assert ($r.Output -match "old-name\.asi") "leftovers: and names the file"
Assert (Test-Path (Join-Path $game "plugins\old-name.asi")) "leftovers: the dry run deleted nothing"

$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "leftovers: the update ran"
Assert (Test-Path (Join-Path $game "plugins\new-name.asi")) "leftovers: the new file is there"
Assert (-not (Test-Path (Join-Path $game "plugins\old-name.asi"))) "leftovers: the old file is gone"
Assert ($r.Output -match "Left over from the previous version") "leftovers: and it is reported"

# Taking it back has to restore the state from before this update, old file
# included - the snapshot covers the deletion like any other change.
$r = Invoke-Mliv (@("remove", "test-rename", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "leftovers: the update was taken back"
Assert (Test-Path (Join-Path $game "plugins\old-name.asi")) "leftovers: the old file came back"
Assert (-not (Test-Path (Join-Path $game "plugins\new-name.asi"))) "leftovers: the new one is gone"

# A file the recipe keeps under the same name is not a leftover.
$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "leftovers: applying the same release twice works"
Assert (Test-Path (Join-Path $game "plugins\new-name.asi")) "leftovers: and does not delete its own file"

$r = Invoke-Mliv (@("remove", "test-rename", "--yes") + $common)

# A file a second recipe owns as well must not be deleted as a leftover. Mods do
# overwrite each other - FusionFix ships its own dinput8.dll over the one the ASI
# loader installed - and dropping it on an update would take the other apart.

@"
{
  "id": "test-shared",
  "name": "Test recipe, shares a file",
  "version": "1.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "loader", "fileName": "loader.dll", "sha256": "$loaderHash", "sizeBytes": $loaderSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "loader.dll", "target": "plugins\\shared.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-shared.json") -Encoding utf8

@"
{
  "id": "test-rename",
  "name": "Test recipe, renames its file",
  "version": "3.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "loader", "fileName": "loader.dll", "sha256": "$loaderHash", "sizeBytes": $loaderSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "loader.dll", "target": "plugins\\shared.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-rename.json") -Encoding utf8

$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
$r = Invoke-Mliv (@("apply", "test-shared", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "shared: the second recipe installed over the same file"

# Release four writes somewhere else, so shared.dll becomes a leftover of
# test-rename - but test-shared still owns it.
@"
{
  "id": "test-rename",
  "name": "Test recipe, renames its file",
  "version": "4.0.0",
  "game": "GtaIV",
  "sources": [
    { "id": "loader", "fileName": "loader.dll", "sha256": "$loaderHash", "sizeBytes": $loaderSize, "urls": [] }
  ],
  "steps": [ { "type": "copyFile", "source": "loader.dll", "target": "plugins\\own.dll" } ]
}
"@ | Set-Content -Path (Join-Path $catalog "test-rename.json") -Encoding utf8

$r = Invoke-Mliv (@("apply", "test-rename", "--yes") + $common)
Assert ($r.ExitCode -eq 0) "shared: the update ran"
Assert (Test-Path (Join-Path $game "plugins\shared.dll")) "shared: the file another recipe owns was kept"
Assert (-not ($r.Output -match "shared\.dll")) "shared: and was not even offered for deletion"

# The counter-check must not report the shared file as changed either: the
# newest owner describes what is on disk, the older record does not.
$r = Invoke-Mliv (@("verify") + $common)
Assert ($r.ExitCode -eq 0) "shared: the counter-check stays quiet about a shared file"

$r = Invoke-Mliv (@("remove", "--all", "--yes") + $common)
Remove-Item (Join-Path $catalog "test-rename.json"), (Join-Path $catalog "test-shared.json") -Force

# --------------------------------------------------------- Steam and Epic

Write-Host "`n== Steam and Epic ==" -ForegroundColor Cyan

# This is code that had never once been executed: the machine it was written on
# has the Rockstar launcher, and neither store was ever asked anything. So both
# get built out of paper here - a libraryfolders.vdf as Steam writes it, an
# appmanifest beside it, an Epic manifest - and detection is pointed at them.

$steam = Join-Path $work "steam"
$library = Join-Path $work "steam-library"
$epicManifests = Join-Path $work "epic-manifests"
$epicGame = Join-Path $work "epic-games\GTAIV"

# The Complete Edition - which is what both stores sell - keeps the game one
# folder below what it calls the install folder: GTAIV\ next to EFLC\.
$steamGame = Join-Path $library "steamapps\common\Grand Theft Auto IV\GTAIV"

New-Item -ItemType Directory -Force -Path (Join-Path $steam "steamapps"), $steamGame, $epicManifests, $epicGame | Out-Null
Set-Content -Path (Join-Path $steamGame "GTAIV.exe") -Value "fake game file" -NoNewline -Encoding utf8
Set-Content -Path (Join-Path $epicGame "GTAIV.exe") -Value "fake game file" -NoNewline -Encoding utf8

# Steam doubles its backslashes in the vdf, keeps the game in whichever library
# had room - here the second one - and leaves entries behind for disks that are
# no longer plugged in. All three have to survive being read.
function Vdf($path) { $path -replace '\\', '\\' }

@"
"libraryfolders"
{
	"0"
	{
		"path"		"$(Vdf $steam)"
		"apps"
		{
		}
	}
	"1"
	{
		"path"		"$(Vdf $library)"
		"apps"
		{
			"12210"		"18375927296"
		}
	}
	"2"
	{
		"path"		"$(Vdf (Join-Path $work 'disk-that-is-gone'))"
	}
}
"@ | Set-Content -Path (Join-Path $steam "steamapps\libraryfolders.vdf") -Encoding utf8

@"
"AppState"
{
	"appid"		"12210"
	"name"		"Grand Theft Auto IV: The Complete Edition"
	"installdir"		"Grand Theft Auto IV"
}
"@ | Set-Content -Path (Join-Path $library "steamapps\appmanifest_12210.acf") -Encoding utf8

@"
{
  "FormatVersion": 0,
  "DisplayName": "Fortnite",
  "InstallLocation": "$(Vdf (Join-Path $work 'epic-games\Fortnite'))"
}
"@ | Set-Content -Path (Join-Path $epicManifests "0000deadbeef.item") -Encoding utf8

@"
{
  "FormatVersion": 0,
  "DisplayName": "Grand Theft Auto IV: The Complete Edition",
  "InstallLocation": "$(Vdf $epicGame)",
  "AppName": "Ghost"
}
"@ | Set-Content -Path (Join-Path $epicManifests "1111c0ffee.item") -Encoding utf8

# Not even valid JSON - a half-written manifest must not take the search down
# with it, because everything found so far would be lost along with it.
Set-Content -Path (Join-Path $epicManifests "2222broken.item") -Value "{ not json" -Encoding utf8

$reportFile = Join-Path $work "detect.json"
$r = Invoke-Mliv @("detect", "--json", "--out", $reportFile,
                   "--steam-path", $steam, "--epic-manifests", $epicManifests)

$report = Get-Content $reportFile -Raw | ConvertFrom-Json
$steamFound = $report.Installs | Where-Object { $_.Path -eq $steamGame }
$epicFound = $report.Installs | Where-Object { $_.Path -eq $epicGame }

Assert ($null -ne $steamFound) "steam: the game is found in the second library"
Assert ($steamFound.Platform -eq "Steam") "steam: and is recognised as Steam"
Assert ($steamFound.FoundVia -match "appmanifest_12210") "steam: the report says which file said so"
Assert ($null -ne $epicFound) "epic: the game is found through the manifest"
Assert ($epicFound.Platform -eq "Epic") "epic: and is recognised as Epic"
Assert (-not ($report.Installs | Where-Object { $_.Path -match "Fortnite" })) "epic: another game's manifest is ignored"

# The folder both stores show the user is the one above the game. Whoever picks
# it by hand has to end up at the game, not at an error message.
$parent = Split-Path -Parent $steamGame
$r = Invoke-Mliv @("detect", "--json", "--out", $reportFile, "--path", $parent)
$report = Get-Content $reportFile -Raw | ConvertFrom-Json
Assert ($report.Installs.Count -eq 1 -and $report.Installs[0].Path -eq $steamGame) `
    "complete edition: a folder named by hand leads down into GTAIV"

# Found by hand, and still recognised as Steam - the update guard depends on
# it, and the manifest sits four levels above the EXE on this layout.
Assert ($report.Installs[0].Platform -eq "Steam") "complete edition: the manifest above it still gives Steam away"

$r = Invoke-Mliv @("guard", "--path", $steamGame)
Assert ($r.Output -match "appmanifest_12210") "complete edition: the update guard finds the manifest too"

# ------------------------------------------------------------------- Views

Write-Host "`n== Views ==" -ForegroundColor Cyan

# The window cannot be looked at from here, and the mistakes that matter in it
# are not build errors: a mistyped resource key, a template that does not parse,
# a binding to a property that was renamed. So every page is built once and laid
# out, with WPF's own binding trace treated as a failure. It reports in the same
# PASS/FAIL form as everything else here, and those lines are counted in.

& $dotnet build (Join-Path $root "tests\UiSmoke\UiSmoke.csproj") -c Debug --nologo -v q | Out-Null

if ($LASTEXITCODE -ne 0) {
    Assert $false "views: the smoke test builds"
} else {
    $smoke = Join-Path $root "tests\UiSmoke\bin\Debug\net10.0-windows\ui-smoke.exe"

    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $smoke (Join-Path $root "catalog") 2>&1 | Out-String
    }
    finally { $ErrorActionPreference = $previous }

    foreach ($line in ($output -split "`r?`n")) {
        if ($line -match '^\s+(PASS|FAIL)\s+(.+?)\s*$') {
            Assert ($matches[1] -eq "PASS") "views: $($matches[2])"
        }
    }
}

# ------------------------------------------------------------------- Result

Write-Host "`n$('=' * 50)"
Write-Host " $script:passed passed, $script:failed failed" -ForegroundColor $(if ($script:failed) { "Red" } else { "Green" })
Write-Host "$('=' * 50)`n"

exit $(if ($script:failed) { 1 } else { 0 })
